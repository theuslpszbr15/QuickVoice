using System.Diagnostics;
using QuickVoice.Core;

namespace QuickVoice;

/// <summary>
/// Speech partials → Engine → Jev → executor, and how early each command fired.
/// Everything runs on the UI thread (the dispatcher), so the engine is never touched concurrently.
/// </summary>
internal sealed class Session
{
    // Calibration knobs: a longer pause tolerates hesitation, a shorter one fires open actions sooner.
    private static readonly TimeSpan PauseAfter = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan UtteranceEndsAfter = TimeSpan.FromSeconds(2);

    public Action OnUtteranceEnd { get; set; } = () => { };
    /// <summary>The floating bar, in the app; null in the terminal-only --text mode.</summary>
    public BarModel? Bar { get; set; }
    /// <summary>The recognizer still hears a voice: the pause waits (Whisper sends words in bursts).</summary>
    public Func<bool> StillSpeaking { get; set; } = () => false;
    /// <summary>Always-listening mode: only speech that starts with one of these phrases is acted on.</summary>
    public IReadOnlyList<string> WakePhrases { get; set; } = [];
    /// <summary>Where each utterance is recorded; null in the terminal-only --text mode.</summary>
    public History? History { get; set; }

    private IDecider decider;
    private readonly LocalDecider local = new();
    private readonly Shortcuts shortcuts;
    private readonly IReadOnlyList<string> appNames;
    private readonly Executor executor;
    private readonly EventLog? log;
    private readonly Engine engine;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private int utterance = 1;
    private string transcript = "";
    private TimeSpan lastWordAt;
    private readonly List<(Command Command, TimeSpan At)> fired = [];
    private string? frontmostHint;
    private Task commands = Task.CompletedTask;
    private CancellationTokenSource? silence;
    private bool awake;
    private int wakeOffset;
    private bool dictating;
    private int dictatedWords;
    private bool dictationCapital;
    /// <summary>What the last utterance did, for "repete".</summary>
    private List<Command> lastCommands = [];
    private bool answerShown;

    private static readonly string[][] DictationStops =
        [["fim", "do", "ditado"], ["para", "o", "ditado"], ["parar", "o", "ditado"], ["parar", "ditado"], ["para", "ditado"],
         ["termina", "o", "ditado"], ["stop", "dictation"], ["end", "dictation"]];

    public Session(IDecider decider, InstalledApps apps, Executor executor, Shortcuts shortcuts, EventLog? log)
    {
        this.decider = decider is LocalDecider ? local : decider;
        this.shortcuts = shortcuts;
        appNames = apps.Names;
        this.executor = executor;
        this.log = log;
        engine = new Engine { AppAliases = LocalDecider.WithSpokenNames(apps.Aliases), Browsers = apps.Browsers };
        ReloadShortcuts();
    }

    /// <summary>A key pasted in the app replaces the one it started with.</summary>
    public void Use(string apiKey) => decider = new JevClient(apiKey);

    /// <summary>A partial transcript: the whole utterance so far.</summary>
    public void Heard(string text)
    {
        if (text == transcript) return;  // only new words count as speech
        if (transcript.Length == 0 && answerShown && Bar is not null)
        {
            Bar.Notice = null;  // the last answer was read; a new question is coming
            answerShown = false;
        }
        transcript = text;
        lastWordAt = clock.Elapsed;
        log?.Write("heard", new() { ["utterance"] = utterance, ["text"] = text });
        WaitForSilence();
        if (WakePhrases.Count > 0 && !awake && !dictating)
        {
            if (WakeEnd(Vocabulary.Words(text)) is not { } end) return;  // not addressed to QuickVoice
            awake = true;
            wakeOffset = end;
        }
        var said = Said();
        if (dictating)
        {
            Bar?.Heard(Display.Words(said, dictatedWords));
            return;
        }
        if (engine.Hear(said) is { } request) Ask(request);
        Bar?.Heard(Display.Words(said, engine.Consumed));
    }

    /// <summary>The recognizer stopped by itself (error, device lost). The silence timer ends the utterance if it is running.</summary>
    public void RecognizerEnded()
    {
        if (silence is null) EndUtterance();
    }

    /// <summary>Listening stopped by hand: fire what the pause would, then close the utterance.</summary>
    public void Stop()
    {
        silence?.Cancel();
        silence = null;
        Handle(engine.Pause());
        EndUtterance();
    }

    /// <summary>A typed command: the whole sentence at once, acted on as if the speaker had just paused.</summary>
    public void Submit(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        if (transcript.Length > 0 || fired.Count > 0)
        {
            silence?.Cancel();
            EndUtterance();
        }
        awake = true;  // typed: addressed to QuickVoice by definition
        Heard(text);
        silence?.Cancel();
        silence = new CancellationTokenSource();
        _ = SilenceAsync(silence.Token, TimeSpan.Zero);
    }

    /// <summary>Feeds a sentence like the mic would (word by word at speaking pace) or typed (all at once), then waits for the commands.</summary>
    public async Task ReplayAsync(string sentence, double wpm, bool typed = false)
    {
        var words = Vocabulary.Words(sentence);
        var done = new TaskCompletionSource();
        OnUtteranceEnd = () =>
        {
            OnUtteranceEnd = () => { };
            done.TrySetResult();
        };
        if (typed) Submit(sentence);
        for (var count = 1; !typed && count <= words.Length; count++)
        {
            Heard(string.Join(' ', words.Take(count)));
            await Task.Delay(TimeSpan.FromSeconds(60 / wpm));
        }
        await done.Task;
        await commands;
    }

    private async void Ask(Engine.Request request)
    {
        var frontmost = frontmostHint ?? Foreground.AppName() ?? "Área de Trabalho";
        var spans = Candidates.Spans(request.Tail);
        var utterance = this.utterance;
        log?.Write("ask", new() { ["utterance"] = utterance, ["seq"] = request.Seq, ["tail"] = request.Text, ["frontmost"] = frontmost });
        // One request per partial, no debounce (~4/s while talking); debounce if cost matters.
        var started = clock.Elapsed;
        try
        {
            // System controls, clicks and the user's shortcuts are read by the local rules even when Jev decides the rest.
            var mine = local.Decide(request.Text, appNames);
            var (decision, tokens) = mine.Action.IsLocalOnly() || ReferenceEquals(decider, local)
                ? (mine, 0)
                : await decider.DecideAsync(request.Text, frontmost, appNames, spans);
            log?.Write("answer", new()
            {
                ["utterance"] = utterance, ["seq"] = request.Seq, ["ms"] = (int)(clock.Elapsed - started).TotalMilliseconds,
                ["tokens"] = tokens, ["action"] = decision.Action.RawValue(), ["confidence"] = decision.Confidence,
                ["app"] = decision.App, ["app_p"] = decision.AppProbability, ["argument"] = decision.Argument,
                ["complete"] = decision.Complete, ["opens_app"] = decision.OpensApp,
                ["p"] = decision.ActionProbabilities.ToDictionary(p => p.Key.RawValue(), p => Math.Round(p.Value, 3)),
            });
            Show(request, decision);
            Bar?.Answered(decision);
            Handle(engine.Receive(decision, request));
        }
        catch (Exception error) when (error is JevException or System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            log?.Write("error", new() { ["utterance"] = utterance, ["seq"] = request.Seq, ["message"] = error.Message });
            Terminal.Out($"\n⚠ Jev: {error.Message}\n");
            if (Bar is not null)
                Bar.Notice = error is JevException.Http { Status: 401 }
                    ? "O Jev recusou a chave: ícone na bandeja → Usar chave do Jev…"
                    : $"O Jev não respondeu: {error.Message}";
        }
    }

    private void Handle(Engine.Step step)
    {
        if (step.Command is Command.Control { Action: SystemAction.DictationStart or SystemAction.DictationStop } dictation)
        {
            log?.Write("fire", new() { ["utterance"] = utterance, ["command"] = dictation.ToString() });
            Bar?.Fire(dictation, Display.Words(Said(), engine.Consumed));
            if (dictation.Action == SystemAction.DictationStart) StartDictation();
            else StopDictation();
            return;  // the words after it are dictated, not asked about
        }
        if (step.Command is Command.Control { Action: SystemAction.Repeat } repeat)
        {
            log?.Write("fire", new() { ["utterance"] = utterance, ["command"] = repeat.ToString() });
            Terminal.Out($"\n⚡ {repeat}\n");
            Bar?.Fire(repeat, Display.Words(Said(), engine.Consumed));
            if (lastCommands.Count == 0 && Bar is not null) Bar.Notice = "Nada para repetir ainda.";
            foreach (var again in lastCommands)
            {
                fired.Add((again, clock.Elapsed));
                commands = RunAfter(commands, again, utterance);
            }
        }
        else if (step.Command is { } command)
        {
            fired.Add((command, clock.Elapsed));
            log?.Write("fire", new() { ["utterance"] = utterance, ["command"] = command.ToString() });
            Terminal.Out($"\n⚡ {command}\n");
            Bar?.Fire(command, Display.Words(Said(), engine.Consumed));
            if (command is Command.OpenApp open) frontmostHint = open.App;
            commands = RunAfter(commands, command, utterance);
        }
        if (step.Request is { } request) Ask(request);
    }

    /// <summary>One at a time: Ctrl+N must not beat Notepad to the front.</summary>
    private async Task RunAfter(Task previous, Command command, int utterance)
    {
        await previous;
        var failure = await executor.RunAsync(command);
        log?.Write("run", new() { ["utterance"] = utterance, ["command"] = command.ToString(), ["ok"] = failure is null, ["error"] = failure });
        if (failure is not null && Bar is not null) Bar.Notice = failure;
        if (failure is null && command is Command.AgentTask agent && Bar is not null)
        {
            Bar.Notice = $"Copilot recebeu: “{agent.Task}”. Acompanhe no terminal ou no VS Code.";
            answerShown = true;
        }
        if (failure is null && command is Command.Answer answer)
        {
            var text = QuickAnswers.Speak(answer, DateTime.UtcNow);
            Terminal.Out($"\n💬 {text}\n");
            if (Bar is not null)
            {
                Bar.Notice = answer.Kind == "math" ? $"{text}  (copiado)" : text;
                answerShown = true;
            }
        }
        if (command is Command.OpenApp open && frontmostHint == open.App) frontmostHint = null;
    }

    private void WaitForSilence()
    {
        silence?.Cancel();
        silence = new CancellationTokenSource();
        _ = SilenceAsync(silence.Token);
    }

    private async Task SilenceAsync(CancellationToken token) => await SilenceAsync(token, PauseAfter);

    private async Task SilenceAsync(CancellationToken token, TimeSpan pauseAfter)
    {
        try
        {
            await Task.Delay(pauseAfter, token);
            while (StillSpeaking()) await Task.Delay(150, token);
            log?.Write("pause", new() { ["utterance"] = utterance });
            if (Bar is not null) Bar.Paused = true;
            if (dictating) Dictate();
            else Handle(engine.Pause());
            await Task.Delay(UtteranceEndsAfter - pauseAfter, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        EndUtterance();
    }

    private void StartDictation()
    {
        dictating = true;
        dictatedWords = engine.Consumed;  // words said after "modo ditado" in the same breath are dictated
        dictationCapital = true;
        if (Bar is not null) Bar.Notice = "Ditado: fale o texto e ele é digitado a cada pausa. Diga “fim do ditado” para parar.";
        Terminal.Out("\n📝 ditado ligado\n");
    }

    private void StopDictation()
    {
        dictating = false;
        if (Bar is not null) Bar.Notice = null;
        Terminal.Out("\n📝 ditado desligado\n");
    }

    /// <summary>At each pause, types what was said since the last one, with spoken punctuation written out.</summary>
    private void Dictate()
    {
        var words = Vocabulary.Words(Said());
        var fresh = words.Skip(dictatedWords).ToArray();
        dictatedWords = words.Length;
        var norm = fresh.Select(Vocabulary.Normalized).ToArray();
        var stop = Enumerable.Range(0, norm.Length)
            .Select(i => (At: i, Phrase: DictationStops.FirstOrDefault(s => i + s.Length <= norm.Length && norm.AsSpan(i, s.Length).SequenceEqual(s))))
            .FirstOrDefault(s => s.Phrase is not null);
        var said = string.Join(' ', stop.Phrase is null ? fresh : fresh[..stop.At]);
        var text = Dictation.Format(said, dictationCapital);
        if (text.Length > 0)
        {
            if (!text.EndsWith('\n')) text += " ";
            dictationCapital = Dictation.EndsSentence(text);
            var command = new Command.TypeText(text);
            fired.Add((command, clock.Elapsed));
            log?.Write("fire", new() { ["utterance"] = utterance, ["command"] = command.ToString() });
            Bar?.Fire(command, Display.Words(Said(), dictatedWords));
            commands = RunAfter(commands, command, utterance);
        }
        if (stop.Phrase is not null) StopDictation();
    }

    /// <summary>What was said to QuickVoice: the utterance after the wake phrase, if one is in use.</summary>
    private string Said() => wakeOffset == 0 ? transcript : string.Join(' ', Vocabulary.Words(transcript).Skip(wakeOffset));

    /// <summary>Where a wake phrase ends: "ok quickvoice abre…", "quick voice, abre…" (said in one word or two).</summary>
    private int? WakeEnd(string[] words)
    {
        var norm = words.Select(w => Vocabulary.Normalized(w).Replace("-", "")).ToArray();
        foreach (var phrase in WakePhrases)
        {
            var wanted = Vocabulary.Normalized(phrase).Replace(" ", "").Replace("-", "");
            for (var start = 0; start < norm.Length; start++)
            {
                var joined = "";
                for (var end = start; end < Math.Min(norm.Length, start + 3); end++)
                {
                    joined += norm[end];
                    if (joined == wanted) return end + 1;
                    if (!wanted.StartsWith(joined)) break;
                }
            }
        }
        return null;
    }

    private void ReloadShortcuts()
    {
        if (shortcuts.Reload()) local.Shortcuts = shortcuts.Phrases;
        if (shortcuts.Error is { } error && Bar is not null) Bar.Notice = error;
    }

    private void EndUtterance()
    {
        silence = null;
        try
        {
            if (transcript.Length == 0 && fired.Count == 0) return;  // the recognizer timing out on silence
            if (WakePhrases.Count > 0 && !awake && fired.Count == 0)
            {
                transcript = "";  // speech not addressed to QuickVoice: forget it
                return;
            }
            var leads = fired.Select(f => (f.Command, Seconds: (lastWordAt - f.At).TotalSeconds)).ToList();  // > 0: before the last word
            log?.Write("end", new()
            {
                ["utterance"] = utterance, ["transcript"] = transcript,
                ["fires"] = leads.Select(l => new Dictionary<string, object> { ["command"] = l.Command.ToString(), ["lead_s"] = Math.Round(l.Seconds, 2) }).ToList(),
            });
            if (leads.Count > 0) Terminal.Out("\n");  // leave the live line
            foreach (var (command, seconds) in leads) Terminal.Out($"✓ {command} · {Display.DescribeLead(seconds)}\n");
            Bar?.Finish(leads.Select(l => Display.DescribeLead(l.Seconds)).ToList());
            var done = fired.Select(f => f.Command).Where(c => c is not Command.Control { Action: SystemAction.Undo }).ToList();
            if (done.Count > 0) lastCommands = done;
            if (fired.Count > 0) History?.Add(transcript, fired.Select(f => f.Command.ToString()));
            fired.Clear();
            transcript = "";
            utterance++;
        }
        finally
        {
            engine.Reset();
            awake = false;
            wakeOffset = 0;
            dictatedWords = 0;
            ReloadShortcuts();
            OnUtteranceEnd();
        }
    }

    private static void Show(Engine.Request request, Decision d)
    {
        var app = d.Action == ActionKind.OpenApp ? $" · {d.App ?? "?"}" : "";
        var argument = d.Argument is { } a ? $" · “{a}”" : "";
        var text = request.Text.Length > 50 ? request.Text[^50..] : request.Text;
        Terminal.Out($"\u001B[2K\r🎙 …{text}  → {d.Action.RawValue()} {d.Confidence:0.00}{app}{argument}");
    }
}
