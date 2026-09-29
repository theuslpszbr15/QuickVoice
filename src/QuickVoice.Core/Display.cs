using System.Globalization;

namespace QuickVoice.Core;

public sealed record SpokenWord(string Text, bool Used);

public static class Display
{
    /// <summary><paramref name="seconds"/> &gt; 0: the command fired that long before the last word.</summary>
    public static string DescribeLead(double seconds) => seconds > 0
        ? string.Format(CultureInfo.GetCultureInfo("pt-BR"), "{0:0.00} s antes de você terminar", seconds)
        : string.Format(CultureInfo.GetCultureInfo("pt-BR"), "{0:0.00} s depois que você parou", -seconds);

    /// <summary>The last <paramref name="limit"/> words, marking the ones already turned into commands.</summary>
    public static List<SpokenWord> Words(string text, int consumed, int limit = 10)
    {
        var words = Vocabulary.Words(text).Select((w, i) => new SpokenWord(w, i < consumed)).ToList();
        return words.Skip(Math.Max(0, words.Count - limit)).ToList();
    }
}
