using System.Diagnostics;
using System.Text.Json;

namespace QuickVoice;

/// <summary>
/// Opt-in local trace (--log): one JSON object per line in %LOCALAPPDATA%\QuickVoice\Logs, never sent anywhere.
/// It holds everything the mic heard, side conversations included.
/// </summary>
internal sealed class EventLog : IDisposable
{
    public string Path { get; }
    public static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickVoice", "Logs");
    private readonly StreamWriter file;
    private readonly Stopwatch started = Stopwatch.StartNew();

    public EventLog()
    {
        var folder = Folder;
        Directory.CreateDirectory(folder);
        Path = System.IO.Path.Combine(folder, $"{DateTime.Now:yyyy-MM-ddTHH-mm-ss}.jsonl");
        file = new StreamWriter(Path, append: false) { AutoFlush = true };
    }

    /// <summary><c>t</c> is seconds since start; null fields are left out.</summary>
    public void Write(string @event, Dictionary<string, object?>? fields = null)
    {
        var line = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["event"] = @event, ["t"] = Math.Round(started.Elapsed.TotalSeconds, 3) };
        foreach (var (key, value) in fields ?? [])
        {
            if (value is not null) line[key] = value is double d ? Math.Round(d, 3) : value;
        }
        file.WriteLine(JsonSerializer.Serialize(line, Core.JevClient.Json));
    }

    public void Dispose() => file.Dispose();
}
