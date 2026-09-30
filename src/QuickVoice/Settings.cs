using System.Text.Json;

namespace QuickVoice;

/// <summary>Preferences from the settings window, in %APPDATA%\QuickVoice\config.json. Command-line options win over them.</summary>
internal sealed class Settings
{
    /// <summary>Speech language, e.g. "en-US"; null follows the Windows speech language.</summary>
    public string? Locale { get; set; }
    /// <summary>"windows" (online dictation, fastest partials) or "whisper" (offline, on this PC).</summary>
    public string Recognizer { get; set; } = "windows";
    /// <summary>Whisper model size: "tiny", "base" or "small" (bigger is more accurate and slower).</summary>
    public string WhisperModel { get; set; } = "base";
    /// <summary>Always listening, acting only on sentences that start with a wake phrase.</summary>
    public bool WakeWord { get; set; }
    public string WakePhrases { get; set; } = "QuickVoice, Quick Voz";
    /// <summary>"auto" (Alt+Espaço, else Ctrl+Alt+Espaço), "alt", "ctrl-alt" or "ctrl-shift".</summary>
    public string Hotkey { get; set; } = "auto";

    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickVoice", "config.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static Settings Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new() : new();
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public IReadOnlyList<string> WakeList =>
        WakePhrases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
