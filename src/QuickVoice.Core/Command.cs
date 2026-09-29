namespace QuickVoice.Core;

/// <summary>A decided command, ready for an executor.</summary>
public abstract record Command
{
    public sealed record OpenApp(string App) : Command;
    public sealed record NewItem : Command;
    public sealed record OpenUrl(Uri Url) : Command;
    public sealed record WebSearch(string Query) : Command;
    public sealed record TypeText(string Text) : Command;

    public sealed override string ToString() => this switch
    {
        OpenApp c => $"abrir {c.App}",
        NewItem => "novo item",
        OpenUrl c => $"abrir {c.Url.OriginalString}",
        WebSearch c => $"pesquisar “{c.Query}”",
        TypeText c => $"digitar “{c.Text}”",
        _ => GetType().Name,
    };

    /// <summary>Where browser commands go.</summary>
    public Uri? WebUrl => this switch
    {
        OpenUrl c => c.Url,
        WebSearch c => new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(c.Query)),
        _ => null,
    };
}

public static class Site
{
    /// <summary>Spoken site to URL: "x dot com" → https://x.com, "meu site do LinkedIn" → https://linkedin.com.</summary>
    public static Uri? Url(string spoken)
    {
        var said = spoken.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var spelled = said.Any(w => Vocabulary.Normalized(w) is "dot" or "ponto" || w.Contains('.'));
        var framing = new HashSet<string>(Vocabulary.Filler.Concat(Vocabulary.SiteWords));
        // Unless spelled out ("x dot com"), "linkedin | no google" names the site, then how to reach it.
        // Only after a real name: in "my website on github" the site comes after "on".
        int? cut = null;
        if (!spelled)
        {
            for (var i = 0; i < said.Length; i++)
            {
                if (Vocabulary.ModifierOpeners.Contains(Vocabulary.Normalized(said[i]))
                    && said[..i].Any(w => !framing.Contains(Vocabulary.Normalized(w))))
                {
                    cut = i;
                    break;
                }
            }
        }
        var words = said[..(cut ?? said.Length)].Where(w => !framing.Contains(Vocabulary.Normalized(w))).ToArray();
        // Several words and no "dot" is a topic ("receita de bolo"), not an address: the engine searches for it instead.
        if (words.Length != 1 && !spelled) return null;
        var host = string.Join(' ', words).ToLowerInvariant()
            .Replace(" dot ", ".")
            .Replace(" ponto ", ".")
            .Replace(" ", "");
        var labels = host.Split('.');
        var valid = host.Length > 0 && labels.All(label =>
            label.Length > 0 && label.All(c => char.IsAscii(c) && (char.IsLetterOrDigit(c) || c == '-')));
        if (!valid) return null;
        return new Uri("https://" + (labels.Length > 1 ? host : host + ".com"));
    }
}
