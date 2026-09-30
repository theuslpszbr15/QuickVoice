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
    /// <summary>The bar: "dark" or "light".</summary>
    public string Theme { get; set; } = "dark";
    /// <summary>The color of what fires: "yellow", "blue", "green" or "pink".</summary>
    public string Accent { get; set; } = "yellow";
    /// <summary>"small", "normal" or "large".</summary>
    public string BarSize { get; set; } = "normal";
    /// <summary>The glow that reacts to the voice: a voice-glow color variant ("colorful", "ocean"…) or "off".</summary>
    public string VoiceGlow { get; set; } = "colorful";
    /// <summary>Asks GitHub for a newer release when the app starts.</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>Where "copilot, …" tasks run: "copilot-cli" (a terminal, any model) or "vscode" (the Copilot chat in agent mode).</summary>
    public string AgentHarness { get; set; } = "copilot-cli";
    /// <summary>A Copilot CLI model id, or "auto".</summary>
    public string AgentModel { get; set; } = "auto";
    /// <summary>Runs tools without asking (--allow-all-tools). Off: every file edit or command waits for a yes.</summary>
    public bool AgentAutonomous { get; set; }
    /// <summary>With the Copilot CLI, also opens the project folder in VS Code to watch the files appear.</summary>
    public bool AgentOpensVsCode { get; set; } = true;
    public string AgentFolder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "QuickVoice Projetos");
    /// <summary>MCP servers (plugins) the agent may use.</summary>
    public List<McpServer> McpServers { get; set; } = [McpServer.Playwright()];

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
