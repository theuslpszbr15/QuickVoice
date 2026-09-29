namespace QuickVoice.Core;

/// <summary>
/// The one request asked on every partial: all questions at once (speculative fan-out),
/// code reads only the answers the chosen action needs. Built from sorted dictionaries so the
/// same options go out in the same order on every run.
/// </summary>
public static class Questions
{
    public const string NoApp = "none";
    public const string NoMatch = "no_match";
    public const int MaxOptions = 254;  // Jev's Choice cap is 255; one slot stays for the escape hatch

    private static SortedDictionary<string, object?> Detail(string what, params string[] examples) =>
        Sorted(("what", what), ("examples", examples));

    private static readonly SortedDictionary<string, object?> Actions = Sorted(
        (ActionKind.OpenApp.RawValue(), Detail("Launch or switch to an application",
            "open slack", "switch to the calendar", "abre o spotify")),
        (ActionKind.NewItem.RawValue(), Detail("Create a new note, document, tab or window in the frontmost app",
            "new tab", "start a new document", "cria uma nota nova")),
        (ActionKind.OpenUrl.RawValue(), Detail("Go to a specific website or web address",
            "go to github dot com", "open wikipedia.org", "entra no youtube")),
        (ActionKind.WebSearch.RawValue(), Detail("Search the web for something",
            "google the weather in lisbon", "look up how tall everest is", "procura o horário do jogo")),
        (ActionKind.TypeText.RawValue(), Detail("Type, write or enter specific words into the frontmost app",
            "type see you tomorrow", "write buy milk", "put meeting notes as the heading", "escreve bom dia", "digita obrigado pela ajuda")),
        (ActionKind.None.RawValue(), Detail("Not a command for the computer yet: filler, thanks, or talk about something",
            "can you", "okay so", "thanks")));

    public static SortedDictionary<string, object?> Body(string tail, string frontmost, IReadOnlyList<string> apps,
                                                         IReadOnlyList<string> spans, string model)
    {
        var questions = Sorted(
            ("action", Question("choice",
                "`transcript` is a live, possibly unfinished voice command to a Windows PC whose frontmost app is `frontmost_app`. What should the computer do?",
                Actions)),
            ("complete", Question("noul",
                "`transcript` is being spoken live. Is the command already fully stated, so that more words would not change what to do or its argument?",
                Sorted(("true", "Action and its argument are fully stated"), ("false", "The speaker is likely mid-command")))));
        if (apps.Count > 0)
        {
            questions["app"] = Question("choice", "Which installed application does `transcript` name as the one to open or use?",
                Options(apps, NoApp, "No application is named"));
            questions["opens_app"] = Question("noul",
                "Does `transcript` ask the computer to open or switch to an application, possibly along with other commands?",
                Sorted(("true", "It asks to open or switch to an app"), ("false", "It does not ask to open an app")));
        }
        if (spans.Count > 0)
        {
            questions["argument"] = Question("choice",
                "Which span of `transcript` is exactly the command's argument: the search query, the website, or the text to type?",
                Options(spans, NoMatch, "The command has no search query, website or text yet"));
        }
        return Sorted(
            ("model", model),
            ("state", Sorted(("transcript", tail), ("frontmost_app", frontmost))),
            ("questions", questions));
    }

    private static SortedDictionary<string, object?> Question(string type, string instructions, SortedDictionary<string, object?> criteria) =>
        Sorted(("type", type), ("instructions", instructions), ("criteria", criteria));

    /// <summary>The option key is the whole meaning (an app name, a span of speech): its value is null.</summary>
    private static SortedDictionary<string, object?> Options(IReadOnlyList<string> keys, string escapeKey, string escapeText)
    {
        var criteria = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in keys.Take(MaxOptions)) criteria.TryAdd(key, null);
        criteria[escapeKey] = escapeText;
        return criteria;
    }

    private static SortedDictionary<string, object?> Sorted(params (string Key, object? Value)[] entries)
    {
        var result = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries) result[key] = value;
        return result;
    }
}
