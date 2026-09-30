using System.Text;

namespace QuickVoice.Core;

/// <summary>Spoken punctuation to written ("oi vírgula tudo bem ponto de interrogação" → "oi, tudo bem?").</summary>
public static class Dictation
{
    private static readonly (string[] Said, string Written)[] Marks =
    [
        (["ponto", "de", "interrogacao"], "?"), (["ponto", "de", "exclamacao"], "!"), (["ponto", "e", "virgula"], ";"),
        (["ponto", "final"], "."), (["dois", "pontos"], ":"), (["novo", "paragrafo"], "\n\n"), (["nova", "linha"], "\n"),
        (["proxima", "linha"], "\n"), (["abre", "parenteses"], " ("), (["fecha", "parenteses"], ")"),
        (["abre", "aspas"], " \u201c"), (["fecha", "aspas"], "\u201d"), (["reticencias"], "..."),
        (["interrogacao"], "?"), (["exclamacao"], "!"), (["virgula"], ","),
        (["question", "mark"], "?"), (["exclamation", "mark"], "!"), (["exclamation", "point"], "!"), (["full", "stop"], "."),
        (["new", "paragraph"], "\n\n"), (["new", "line"], "\n"), (["semicolon"], ";"), (["colon"], ":"), (["comma"], ","),
        (["period"], "."), (["ponto"], "."),
    ];

    /// <summary>
    /// Punctuation joins the word before it, and a sentence starts with a capital after ".", "?", "!" or a new line.
    /// <paramref name="capitalizeFirst"/> for a new text; typing into the middle of one keeps the first letter as said.
    /// A lone "ponto" is a period only in <paramref name="bareDot"/> mode: in "digita site ponto com" it is a word.
    /// </summary>
    public static string Format(string spoken, bool capitalizeFirst = false, bool bareDot = true)
    {
        var words = Vocabulary.Words(spoken);
        var norm = words.Select(Vocabulary.Normalized).ToArray();
        var text = new StringBuilder();
        var capital = capitalizeFirst;
        for (var i = 0; i < words.Length;)
        {
            var mark = Marks.FirstOrDefault(m => i + m.Said.Length <= norm.Length && norm.AsSpan(i, m.Said.Length).SequenceEqual(m.Said)
                                                 && (bareDot || m.Said is not ["ponto"]));
            if (mark.Said is not null)
            {
                var written = mark.Written;
                if (written.StartsWith(' ') && text.Length == 0) written = written.TrimStart();
                text.Append(written);
                if (written is "." or "?" or "!" || written.EndsWith('\n')) capital = true;
                i += mark.Said.Length;
                continue;
            }
            var word = words[i];
            var previous = text.Length > 0 ? text[^1] : '\n';
            if (previous is not ('\n' or ' ' or '(' or '\u201c')) text.Append(' ');
            text.Append(capital && word.Length > 0 ? char.ToUpper(word[0]) + word[1..] : word);
            capital = false;
            i++;
        }
        return text.ToString();
    }

    /// <summary>Whether a finished sentence was typed last, so the next spoken chunk starts with a capital.</summary>
    public static bool EndsSentence(string typed) =>
        typed.Length == 0 || typed.TrimEnd(' ') is var t && (t.Length == 0 || t[^1] is '.' or '?' or '!' or '\n');
}
