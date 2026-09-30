namespace QuickVoice.Core;

/// <summary>
/// What the computer can do. Closed actions (OpenApp, NewItem) pick from a known list, so they may fire mid-sentence.
/// Open actions carry free text: only the pause says the text is done ("search norbert" vs "search norbert wiener").
/// Control, Click and Shortcut come only from the local rules (Jev is never asked about them) and act at the pause.
/// </summary>
public enum ActionKind { OpenApp, NewItem, OpenUrl, WebSearch, TypeText, Control, Click, Shortcut, CloseApp, OpenFolder, OpenRecent, Answer, None }

public static class ActionKinds
{
    public static readonly ActionKind[] All = Enum.GetValues<ActionKind>();

    /// <summary>The actions Jev is asked to choose among; the rest come only from the local rules.</summary>
    public static readonly ActionKind[] Jev =
        [ActionKind.OpenApp, ActionKind.NewItem, ActionKind.OpenUrl, ActionKind.WebSearch, ActionKind.TypeText, ActionKind.None];

    public static bool IsLocalOnly(this ActionKind action) =>
        action is ActionKind.Control or ActionKind.Click or ActionKind.Shortcut or ActionKind.CloseApp
            or ActionKind.OpenFolder or ActionKind.OpenRecent or ActionKind.Answer;

    /// <summary>The wire name Jev knows the action by.</summary>
    public static string RawValue(this ActionKind action) => action switch
    {
        ActionKind.OpenApp => "open_app",
        ActionKind.NewItem => "new_item",
        ActionKind.OpenUrl => "open_url",
        ActionKind.WebSearch => "web_search",
        ActionKind.TypeText => "type_text",
        ActionKind.Control => "control",
        ActionKind.Click => "click",
        ActionKind.Shortcut => "shortcut",
        ActionKind.CloseApp => "close_app",
        ActionKind.OpenFolder => "open_folder",
        ActionKind.OpenRecent => "open_recent",
        ActionKind.Answer => "answer",
        _ => "none",
    };

    public static ActionKind? Parse(string raw) => All.Cast<ActionKind?>().FirstOrDefault(a => a!.Value.RawValue() == raw);

    /// <summary>Short label for the floating bar.</summary>
    public static string Label(this ActionKind action) => action switch
    {
        ActionKind.OpenApp => "abrir app",
        ActionKind.NewItem => "novo item",
        ActionKind.OpenUrl => "abrir site",
        ActionKind.WebSearch => "pesquisar",
        ActionKind.TypeText => "digitar",
        ActionKind.Control => "sistema",
        ActionKind.Click => "clicar",
        ActionKind.Shortcut => "atalho",
        ActionKind.CloseApp => "fechar app",
        ActionKind.OpenFolder => "abrir pasta",
        ActionKind.OpenRecent => "arquivo recente",
        ActionKind.Answer => "responder",
        _ => "ainda não",
    };
}
