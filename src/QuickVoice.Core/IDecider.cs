namespace QuickVoice.Core;

/// <summary>Reads a transcript tail and says what the computer should do: Jev, or the free local rules.</summary>
public interface IDecider
{
    Task<(Decision Decision, int Tokens)> DecideAsync(string tail, string frontmost, IReadOnlyList<string> apps,
                                                      IReadOnlyList<string> spans, CancellationToken cancel = default);
}
