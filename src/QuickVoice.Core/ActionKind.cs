namespace QuickVoice.Core;

/// <summary>
/// What the computer can do. Closed actions (OpenApp, NewItem) pick from a known list, so they may fire mid-sentence.
/// Open actions carry free text: only the pause says the text is done ("search norbert" vs "search norbert wiener").
/// </summary>
public enum ActionKind { OpenApp, NewItem, OpenUrl, WebSearch, TypeText, None }

public static class ActionKinds
{
    public static readonly ActionKind[] All = Enum.GetValues<ActionKind>();

    /// <summary>The wire name Jev knows the action by.</summary>
    public static string RawValue(this ActionKind action) => action switch
    {
        ActionKind.OpenApp => "open_app",
        ActionKind.NewItem => "new_item",
        ActionKind.OpenUrl => "open_url",
        ActionKind.WebSearch => "web_search",
        ActionKind.TypeText => "type_text",
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
        _ => "ainda não",
    };
}
