namespace QuickVoice.Core;

/// <summary>
/// The local rules answer first and at once; a model (Laya, on this PC) reads only what they don't understand,
/// and only once the partials settle: a slow model's read of a half sentence ("quero…") is stale before it arrives.
/// One model request at a time, newest first. While the model is not running (loading, not installed) the rules' answer stands.
/// </summary>
public sealed class HybridDecider(LocalDecider local, IDecider model, TimeSpan? settle = null) : IDecider
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);

    private readonly TimeSpan settle = settle ?? TimeSpan.FromMilliseconds(450);
    private readonly SemaphoreSlim gate = new(1, 1);
    private int newest;
    private long retryAt;

    public async Task<(Decision Decision, int Tokens)> DecideAsync(string tail, string frontmost, IReadOnlyList<string> apps,
                                                                   IReadOnlyList<string> spans, CancellationToken cancel = default)
    {
        var ticket = Interlocked.Increment(ref newest);
        var mine = local.Decide(tail, apps);
        if (mine.Action != ActionKind.None || Environment.TickCount64 < Interlocked.Read(ref retryAt)) return (mine, 0);
        if (settle > TimeSpan.Zero) await Task.Delay(settle, cancel).ConfigureAwait(false);
        if (ticket != Volatile.Read(ref newest)) return (mine, 0);  // still talking
        await gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            if (ticket != Volatile.Read(ref newest)) return (mine, 0);  // a newer partial is waiting: it is the one worth reading
            return await model.DecideAsync(tail, frontmost, apps, spans, cancel).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or JevException.Http || error is TaskCanceledException && !cancel.IsCancellationRequested)
        {
            Interlocked.Exchange(ref retryAt, Environment.TickCount64 + (long)RetryAfter.TotalMilliseconds);
            return (mine, 0);
        }
        finally
        {
            gate.Release();
        }
    }
}
