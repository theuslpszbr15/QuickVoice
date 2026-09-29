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

    private IDecider decider;
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

    public Session(IDecider decider, InstalledApps apps, Executor executor, EventLog? log)
    {
        this.decider = decider;
        appNames = apps.Names;
        this.executor = executor;
        this.log = log;
        engine = new Engine { AppAliases = LocalDecider.WithSpokenNames(apps.Aliases), Browsers = apps.Browsers };
    }

    /// <summary>A key pasted in the app replaces the one it started with.</summary>
    public void Use(string apiKey) => decider = new JevClient(apiKey);

    /// <summary>A partial transcript: the whole utterance so far.</summary>
    public void Heard(string text)
    {
        if (text == transcript) return;  // only new words count as speech
        transcript = text;
        lastWordAt = clock.Elapsed;
        log?.Write("heard", new() { ["utterance"] = utterance, ["text"] = text });
        WaitForSilence();
        if (engine.Hear(text) is { } request) Ask(request);
        Bar?.Heard(Display.Words(text, engine.Consumed));
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
            var (decision, tokens) = await decider.DecideAsync(request.Text, frontmost, appNames, spans);
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
        if (step.Command is { } command)
        {
            fired.Add((command, clock.Elapsed));
            log?.Write("fire", new() { ["utterance"] = utterance, ["command"] = command.ToString() });
            Terminal.Out($"\n⚡ {command}\n");
            Bar?.Fire(command, Display.Words(transcript, engine.Consumed));
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
            log?.Write("pause", new() { ["utterance"] = utterance });
            if (Bar is not null) Bar.Paused = true;
            Handle(engine.Pause());
            await Task.Delay(UtteranceEndsAfter - pauseAfter, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        EndUtterance();
    }

    private void EndUtterance()
    {
        silence = null;
        try
        {
            if (transcript.Length == 0 && fired.Count == 0) return;  // the recognizer timing out on silence
            var leads = fired.Select(f => (f.Command, Seconds: (lastWordAt - f.At).TotalSeconds)).ToList();  // > 0: before the last word
            log?.Write("end", new()
            {
                ["utterance"] = utterance, ["transcript"] = transcript,
                ["fires"] = leads.Select(l => new Dictionary<string, object> { ["command"] = l.Command.ToString(), ["lead_s"] = Math.Round(l.Seconds, 2) }).ToList(),
            });
            if (leads.Count > 0) Terminal.Out("\n");  // leave the live line
            foreach (var (command, seconds) in leads) Terminal.Out($"✓ {command} · {Display.DescribeLead(seconds)}\n");
            Bar?.Finish(leads.Select(l => Display.DescribeLead(l.Seconds)).ToList());
            fired.Clear();
            transcript = "";
            utterance++;
        }
        finally
        {
            engine.Reset();
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
