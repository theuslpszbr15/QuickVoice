namespace QuickVoice.Core;

/// <summary>
/// Turns a live transcript plus Jev decisions into commands.
/// Every partial transcript becomes a request for the words not yet used ("tail").
/// Closed actions fire once <see cref="StableCount"/> partials in a row agree; open actions fire when the speaker pauses.
/// Firing consumes the tail, so the rest of the sentence becomes the next command.
/// </summary>
public sealed class Engine
{
    public sealed record Request(int Seq, int Epoch, int Consumed, string[] Tail)
    {
        public string Text => string.Join(' ', Tail);
    }

    /// <param name="Request">Leftover words to ask about right away (the speaker may already be silent).</param>
    public sealed record Step(Command? Command = null, Request? Request = null);

    public double EarlyThreshold { get; set; } = 0.85;
    /// <summary>Opening an app is cheap to undo: a lower bar mid-sentence, but only for an app named beyond doubt (<see cref="SureApp"/>).</summary>
    public double OpenAppThreshold { get; set; } = 0.8;
    public double SureApp { get; set; } = 0.95;
    public double PauseThreshold { get; set; } = 0.7;
    public int StableCount { get; set; } = 2;
    public int Consumed { get; private set; }
    public IReadOnlyDictionary<string, List<string>> AppAliases { get; set; } = new Dictionary<string, List<string>>();
    public IReadOnlySet<string> Browsers { get; set; } = new HashSet<string>();

    private string[] words = [];
    private int epoch;
    private int seq;
    private string[] lastTail = [];
    private (Request Request, Decision Decision)? latest;
    private bool paused;
    private (Command Command, int Count)? streak;
    private Command? lastFired;

    /// <summary>A new partial from speech recognition: the whole utterance so far.</summary>
    public Request? Hear(string transcript)
    {
        // Word-count offsets: a recognizer rewrite that merges words ("x dot com" → "x.com") shifts them.
        words = Vocabulary.Words(transcript);
        paused = false;
        return NextRequest();
    }

    /// <summary>The speaker went quiet.</summary>
    public Step Pause()
    {
        paused = true;
        if (latest is not { } l || l.Request.Seq != seq) return new Step();  // newest answer still in flight
        return Fire(OnPause(l.Decision), l.Decision.Argument, l.Request);
    }

    /// <summary>Jev answered <paramref name="request"/>.</summary>
    public Step Receive(Decision decision, Request request)
    {
        if (request.Epoch != epoch || request.Seq <= (latest?.Request.Seq ?? 0)) return new Step();  // stale or late
        latest = (request, decision);
        var command = paused && request.Seq == seq ? OnPause(decision) : OnPartial(decision);
        return Fire(command, decision.Argument, request);
    }

    /// <summary>Speech recognition restarted: a fresh utterance.</summary>
    public void Reset()
    {
        words = [];
        Consumed = 0;
        epoch++;
        lastTail = [];
        latest = null;
        paused = false;
        streak = null;
        lastFired = null;
    }

    private Request? NextRequest()
    {
        var tail = words.Skip(Consumed).ToArray();
        if (tail.Length == 0 || tail.SequenceEqual(lastTail)) return null;
        lastTail = tail;
        seq++;
        return new Request(seq, epoch, Consumed, tail);
    }

    private Command? OnPartial(Decision d)
    {
        // Either signal may carry "open the app": the action choice, or the yes/no that survives a second command.
        var asksForApp = Math.Max(d.OpensApp, d.Action == ActionKind.OpenApp ? d.Confidence : 0);
        Command? candidate =
            asksForApp >= OpenAppThreshold && d.AppProbability >= SureApp && d.App is { } app ? new Command.OpenApp(app)
            : d.Action == ActionKind.NewItem && d.Confidence >= EarlyThreshold ? new Command.NewItem()
            : null;
        if (candidate is null)
        {
            streak = null;
            return null;
        }
        var count = (streak is { } s && s.Command.Equals(candidate) ? s.Count : 0) + 1;
        streak = (candidate, count);
        return count >= StableCount ? candidate : null;
    }

    /// <summary>Actions grouped by what ends up on screen: going to a site, searching for it, or opening the browser it names.</summary>
    private ActionKind[][] Outcomes(Decision d)
    {
        var namesBrowser = d.App is { } app && Browsers.Contains(app);
        ActionKind[][] local = [[ActionKind.Control], [ActionKind.Click], [ActionKind.Shortcut]];
        return namesBrowser
            ? [[ActionKind.OpenUrl, ActionKind.WebSearch, ActionKind.OpenApp], [ActionKind.NewItem], [ActionKind.TypeText], .. local]
            : [[ActionKind.OpenUrl, ActionKind.WebSearch], [ActionKind.OpenApp], [ActionKind.NewItem], [ActionKind.TypeText], .. local];
    }

    /// <summary>
    /// Acts on the chance of an outcome, not on how concentrated one label is: "abre o LinkedIn no Google" splits
    /// 0.72 site / 0.15 browser / 0.13 search, so the label's confidence is 0.66 though site or search both get there.
    /// </summary>
    private Command? OnPause(Decision d)
    {
        var best = Outcomes(d)
            .Select(outcome => (Outcome: outcome, Chance: outcome.Sum(d.Probability)))
            .MaxBy(o => o.Chance);
        if (best.Outcome is null || best.Chance < PauseThreshold) return null;
        foreach (var action in Order(best.Outcome, d))
        {
            if (CommandFor(action, d) is { } command) return command;  // a "site" that is no address falls back to search
        }
        return null;
    }

    /// <summary>
    /// Likeliest first. A browser that joined the web outcome only lends its chance: the site or search named wins
    /// ("abre o linkedin no google" → linkedin.com), unless the argument is the browser itself ("abre o google").
    /// </summary>
    private List<ActionKind> Order(ActionKind[] outcome, Decision d)
    {
        var ranked = outcome.OrderByDescending(d.Probability).ToList();
        if (!outcome.Contains(ActionKind.WebSearch) || !outcome.Contains(ActionKind.OpenApp) || d.App is not { } app) return ranked;
        var names = new[] { app }.Concat(AppAliases.GetValueOrDefault(app) ?? []);
        var browserWords = names.SelectMany(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Select(Vocabulary.Normalized).ToHashSet();
        var argumentWords = (d.Argument ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Vocabulary.Normalized);
        var web = ranked.Where(a => a != ActionKind.OpenApp).ToList();
        return argumentWords.All(browserWords.Contains) ? [ActionKind.OpenApp, .. web] : [.. web, ActionKind.OpenApp];
    }

    private Command? CommandFor(ActionKind action, Decision d) => action switch
    {
        ActionKind.OpenApp => d.AppProbability >= PauseThreshold && d.App is { } app ? new Command.OpenApp(app) : null,
        ActionKind.NewItem => new Command.NewItem(),
        ActionKind.OpenUrl => d.Argument is { } a && Site.Url(a) is { } url ? new Command.OpenUrl(url) : null,
        ActionKind.WebSearch => d.Argument is { } q ? new Command.WebSearch(q) : null,
        ActionKind.TypeText => d.Argument is { } t ? new Command.TypeText(Dictation.Format(t, bareDot: false)) : null,
        ActionKind.Control => d.Detail is { } code ? Controls.Decode(code) : null,
        ActionKind.Click => d.Argument is { } target ? new Command.Click(target) : null,
        ActionKind.Shortcut => d.Detail is { } name ? new Command.Shortcut(name) : null,
        _ => null,
    };

    /// <summary>
    /// Words said after a command fired that start no new one still belong to it: "cria uma | nota nova" must not
    /// create a second note. They are skipped up to the next "e" or command verb, and what follows is asked about again.
    /// </summary>
    private Step Fire(Command? command, string? argument, Request request)
    {
        if (command is null) return new Step();
        var newCommand = ClauseStart(request.Tail);
        if (command.Equals(lastFired) && newCommand > 0)
            return new Step(null, Consume(newCommand, request));
        lastFired = command;
        var used = WordsUsed(command, argument, AliasesOf(command), request.Tail);
        return new Step(command, Consume(used, request));
    }

    private Request? Consume(int count, Request request)
    {
        Consumed = request.Consumed + count;
        epoch++;
        latest = null;
        streak = null;
        lastTail = [];
        return NextRequest();
    }

    /// <summary>Where the next command starts in the words after a fire: the first "e"/"and" or command verb.</summary>
    private static int ClauseStart(string[] tail)
    {
        var index = Array.FindIndex(tail, w =>
        {
            var n = Vocabulary.Normalized(w);
            return Vocabulary.Connectives.Contains(n) || Vocabulary.CommandVerbs.Contains(n);
        });
        return index >= 0 ? index : tail.Length;
    }

    private List<string> AliasesOf(Command command) =>
        command is Command.OpenApp open ? [open.App, .. AppAliases.GetValueOrDefault(open.App) ?? []] : [];

    /// <summary>
    /// A command ends where its own words end, so the one chained after it survives even when both were said
    /// before anything fired: open ends at the app's name ("abre as notas | e digita oi"), search, site and typing at
    /// their argument ("search cake recipes | and open notes").
    /// </summary>
    public static int WordsUsed(Command command, string? argument, IReadOnlyList<string> aliases, string[] tail)
    {
        var words = tail.Select(Vocabulary.Normalized).ToArray();
        int? own = command switch
        {
            Command.OpenApp => Mention(aliases, words),
            Command.NewItem => null,  // no span for "a new note", so a command said in the same breath after it is lost
            _ => End((argument ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Vocabulary.Normalized).ToArray(), words),
        };
        return own is { } o ? ModifierEnd(o, words) : tail.Length;
    }

    /// <summary>
    /// "abre o linkedin | no google por favor": what follows a command and only says how, where or please belongs to it,
    /// so it never becomes a command of its own. Stops at a connective ("e", "and") or another command's verb.
    /// </summary>
    private static int ModifierEnd(int start, string[] words)
    {
        var end = start;
        var inModifier = false;
        while (end < words.Length)
        {
            var word = words[end];
            if (Vocabulary.Connectives.Contains(word) || Vocabulary.CommandVerbs.Contains(word)) break;
            if (Vocabulary.ModifierOpeners.Contains(word)) inModifier = true;
            else if (!inModifier && !Vocabulary.Filler.Contains(word)) break;
            end++;
        }
        return end;
    }

    /// <summary>
    /// Where the earliest full name ends, else the earliest first word ("google" for Google Chrome), else any
    /// distinctive word of it ("chrome"), plus a trailing "app".
    /// </summary>
    private static int? Mention(IReadOnlyList<string> aliases, string[] words)
    {
        var names = aliases.Select(a => a.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Vocabulary.Normalized).ToArray()).ToList();
        var end = names.Select(n => End(n, words)).Where(e => e is not null).Min()
            ?? names.Where(n => n.Length > 0).Select(n => End([n[0]], words)).Where(e => e is not null).Min()
            ?? names.SelectMany(n => n).Where(w => w.Length >= 4 && !Vocabulary.Filler.Contains(w)).Select(w => End([w], words)).Where(e => e is not null).Min();
        if (end is not { } e) return null;
        while (e < words.Length && Vocabulary.AppWords.Contains(words[e])) e++;
        return e;
    }

    private static int? End(string[] span, string[] words)
    {
        if (span.Length == 0 || span.Length > words.Length) return null;
        for (var start = 0; start <= words.Length - span.Length; start++)
        {
            if (words.AsSpan(start, span.Length).SequenceEqual(span)) return start + span.Length;
        }
        return null;
    }
}
