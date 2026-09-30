using System.Text.Json;

namespace QuickVoice;

/// <summary>What was said and done, newest first, in %LOCALAPPDATA%\QuickVoice\historico.json (never leaves the PC).</summary>
internal sealed class History
{
    public sealed record Entry(DateTime At, string Said, IReadOnlyList<string> Done);

    private const int Keep = 200;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickVoice", "historico.json");

    private List<Entry> entries = Load();

    public IReadOnlyList<Entry> Entries => entries;

    public void Add(string said, IEnumerable<string> done)
    {
        entries.Insert(0, new Entry(DateTime.Now, said, done.ToList()));
        if (entries.Count > Keep) entries = entries[..Keep];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // the history is a convenience: a full or locked disk must not stop commands
        }
    }

    /// <summary>One entry, shredded in the history window.</summary>
    public void Remove(Entry entry)
    {
        if (!entries.Remove(entry)) return;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Clear()
    {
        entries = [];
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static List<Entry> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
