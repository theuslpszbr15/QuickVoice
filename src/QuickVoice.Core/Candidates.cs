namespace QuickVoice.Core;

/// <summary>
/// Code proposes, Jev picks: every short contiguous span of the tail may be the argument,
/// and the chosen one is copied verbatim. Jev never writes text.
/// </summary>
public static class Candidates
{
    public const int MaxSpanWords = 8;
    public const int Window = 30;

    public static List<string> Spans(IReadOnlyList<string> words)
    {
        // Last 30 words only (≤212 spans, under Jev's 255-option cap); a long monologue loses its start.
        var tail = words.Skip(Math.Max(0, words.Count - Window))
            .Select(Vocabulary.TrimPunctuation)
            .Where(w => w.Length > 0)
            .ToArray();
        // Jev must never search for, or type, the command itself: no span of only framing words ("e pesquisar"),
        // none starting with the command ("digita ls"), none ending mid-phrase ("receita de").
        var framing = new HashSet<string>(Vocabulary.Filler.Concat(Vocabulary.CommandVerbs));
        var leading = new HashSet<string>(Vocabulary.CommandVerbs.Concat(Vocabulary.Connectives));
        var trailing = new HashSet<string>(Vocabulary.Connectives.Concat(Vocabulary.Prepositions));
        var seen = new HashSet<string>();
        var spans = new List<string>();
        for (var start = 0; start < tail.Length; start++)
        {
            for (var end = start; end < Math.Min(start + MaxSpanWords, tail.Length); end++)
            {
                var span = tail[start..(end + 1)];
                var normalized = span.Select(Vocabulary.Normalized).ToArray();
                if (normalized.All(framing.Contains) || leading.Contains(normalized[0]) || trailing.Contains(normalized[^1])) continue;
                var text = string.Join(' ', span);
                if (seen.Add(text)) spans.Add(text);
            }
        }
        return spans;
    }
}
