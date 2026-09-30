using System.Globalization;
using System.Text;

namespace QuickVoice.Core;

/// <summary>Words that frame a command rather than carry its argument, in English and Portuguese (normalized).</summary>
public static class Vocabulary
{
    public static readonly HashSet<string> Filler =
    [
        "a", "an", "the", "and", "or", "to", "for", "of", "on", "in", "at", "my", "me", "please", "can", "you", "up", "now", "just", "then",
        "o", "os", "as", "um", "uma", "e", "ou", "de", "do", "da", "dos", "das", "no", "na", "nos", "nas", "em", "pra", "para", "por",
        "favor", "meu", "minha", "meus", "minhas", "mim", "ai", "entao", "depois", "agora",
    ];

    public static readonly HashSet<string> CommandVerbs =
    [
        "search", "look", "find", "type", "write", "open", "go", "enter",
        "pesquisa", "pesquisar", "pesquise", "procura", "procurar", "busca", "buscar",
        "digita", "digitar", "digite", "escreve", "escrever", "escreva", "abre", "abrir", "abra", "vai", "ir", "entra", "entrar",
        "fecha", "fechar", "feche", "minimiza", "maximiza", "aumenta", "diminui", "abaixa", "clica", "clique", "clicar",
        "click", "close", "minimize", "maximize", "mute", "volume",
    ];

    /// <summary>An argument never starts with these ("digita | ls", "e | bom dia")...</summary>
    public static readonly HashSet<string> Connectives = ["e", "and", "then", "depois", "ou", "or", "entao"];

    /// <summary>...nor ends on a dangling one ("receita de |"). Not "com": "x dot com".</summary>
    public static readonly HashSet<string> Prepositions =
        ["de", "do", "da", "dos", "das", "of", "to", "para", "pra", "em", "no", "na", "nos", "nas", "for", "in", "on", "at"];

    public static readonly HashSet<string> SiteWords = ["site", "website", "page", "pagina", "link"];

    /// <summary>Open a phrase that only says how or where ("no Google", "pelo Chrome", "with Edge").</summary>
    public static readonly HashSet<string> ModifierOpeners =
        ["pelo", "pela", "pelos", "pelas", "no", "na", "nos", "nas", "em", "com", "via", "usando", "using", "with", "in", "on", "through", "by"];

    /// <summary>Said right after an app's name ("the notes app"): part of the mention.</summary>
    public static readonly HashSet<string> AppWords = ["app", "application", "aplicativo", "programa"];

    /// <summary>Lowercased, without accents or edge punctuation: "Página," → "pagina".</summary>
    public static string Normalized(string word)
    {
        var decomposed = TrimPunctuation(word).Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) folded.Append(c);
        }
        return folded.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    public static string TrimPunctuation(string word)
    {
        int start = 0, end = word.Length;
        while (start < end && char.IsPunctuation(word[start])) start++;
        while (end > start && char.IsPunctuation(word[end - 1])) end--;
        return word[start..end];
    }

    public static string[] Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
