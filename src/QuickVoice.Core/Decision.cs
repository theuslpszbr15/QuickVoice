namespace QuickVoice.Core;

/// <summary>Jev's typed answers for one transcript tail.</summary>
public sealed class Decision
{
    public ActionKind Action { get; init; }
    public double Confidence { get; init; }
    public IReadOnlyDictionary<ActionKind, double> ActionProbabilities { get; init; } = new Dictionary<ActionKind, double>();
    public string? App { get; init; }
    public double AppProbability { get; init; }
    public string? Argument { get; init; }
    /// <summary>Which system control (<see cref="Controls"/>) or custom shortcut the words name; the local rules fill it.</summary>
    public string? Detail { get; init; }
    /// <summary>"Is the command fully stated?" Shown, never used to fire: it swings on dangling words ("for", "and").</summary>
    public double Complete { get; init; }
    /// <summary>
    /// Yes/no "does it ask to open an app?". Unlike the single action choice, it stays high when a second
    /// command follows ("abre as notas e digita…"), where the choice splits between two valid actions.
    /// </summary>
    public double OpensApp { get; init; }

    /// <summary>Jev's probability for <paramref name="action"/>; a decision built by hand carries only the chosen action's confidence.</summary>
    public double Probability(ActionKind action) =>
        ActionProbabilities.TryGetValue(action, out var p) ? p : action == Action ? Confidence : 0;

    /// <summary>The <paramref name="count"/> likeliest actions, most likely first; ties by name so the bar never flickers.</summary>
    public List<(ActionKind Action, double Probability)> Top(int count) =>
        ActionProbabilities
            .OrderByDescending(p => p.Value)
            .ThenBy(p => p.Key.RawValue(), StringComparer.Ordinal)
            .Take(count)
            .Select(p => (p.Key, p.Value))
            .ToList();

    public static Decision FromAnswers(IReadOnlyDictionary<string, JevAnswer> answers)
    {
        if (!answers.TryGetValue("action", out var answer) || answer.Choice is not { } choice)
            throw new JevException.MissingAnswer("action");
        var action = ActionKinds.Parse(choice) ?? throw new JevException.UnexpectedAnswer("action", choice);
        answers.TryGetValue("app", out var appAnswer);
        var app = appAnswer?.Choice is { } a && a != Questions.NoApp ? a : null;
        answers.TryGetValue("argument", out var argumentAnswer);
        answers.TryGetValue("complete", out var complete);
        answers.TryGetValue("opens_app", out var opensApp);
        return new Decision
        {
            Action = action,
            Confidence = answer.Confidence ?? 0,
            ActionProbabilities = (answer.Probabilities ?? [])
                .Select(p => (Action: ActionKinds.Parse(p.Key), p.Value))
                .Where(p => p.Action is not null)
                .ToDictionary(p => p.Action!.Value, p => p.Value),
            App = app,
            AppProbability = app is not null && appAnswer?.Probabilities?.TryGetValue(app, out var ap) == true ? ap : 0,
            Argument = argumentAnswer?.Choice is { } arg && arg != Questions.NoMatch ? arg : null,
            Complete = complete?.Noul ?? 0,
            OpensApp = opensApp?.Noul ?? 0,
        };
    }
}
