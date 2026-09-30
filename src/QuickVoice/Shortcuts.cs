using System.Diagnostics;
using System.Text.Json;

namespace QuickVoice;

/// <summary>
/// The user's own commands, from %APPDATA%\QuickVoice\atalhos.json: phrases to say and what to do, in order
/// (open a folder, file, program or site; type a text; press keys). Reloaded whenever the file changes.
/// </summary>
internal sealed class Shortcuts
{
    public sealed record Entry(string Name, IReadOnlyList<string> Phrases, string? Open, string? Type, string? Keys);

    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickVoice", "atalhos.json");

    private const string Sample = """
        // Atalhos do QuickVoice: diga uma das "frases" e ele faz o resto, nesta ordem:
        //   "abrir":   pasta, arquivo, programa ou site (aceita %USERPROFILE% e afins)
        //   "digitar": um texto (\n quebra a linha)
        //   "teclas":  atalhos de teclado, ex. "ctrl+shift+s" ou "ctrl+a ctrl+c"
        // Salve o arquivo: o QuickVoice recarrega sozinho.
        [
          {
            "frases": ["abre os downloads", "minha pasta de downloads"],
            "abrir": "%USERPROFILE%\\Downloads"
          },
          {
            "frases": ["assinatura do email"],
            "digitar": "Atenciosamente,\nSeu nome"
          },
          {
            "frases": ["salva tudo"],
            "teclas": "ctrl+shift+s"
          }
        ]
        """;

    public IReadOnlyList<Entry> Entries { get; private set; } = [];
    public string? Error { get; private set; }
    private DateTime loadedAt;

    public IReadOnlyList<(string Name, IReadOnlyList<string> Phrases)> Phrases =>
        Entries.Select(e => (e.Name, e.Phrases)).ToList();

    /// <summary>Creates the sample file on first use. True when the entries changed.</summary>
    public bool Reload()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, Sample);
            }
            var stamp = File.GetLastWriteTimeUtc(FilePath);
            if (stamp == loadedAt) return false;
            loadedAt = stamp;
            Entries = Parse(File.ReadAllText(FilePath));
            Error = null;
            return true;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Error = $"atalhos.json: {error.Message}";
            return false;
        }
    }

    public static List<Entry> Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var entries = new List<Entry>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var phrases = Strings(item, "frases", "frase", "phrases", "say");
            if (phrases.Count == 0) continue;
            entries.Add(new Entry(phrases[0], phrases, Text(item, "abrir", "open"), Text(item, "digitar", "type"), Text(item, "teclas", "keys")));
        }
        return entries;
    }

    public async Task RunAsync(string name, Action<string> type)
    {
        var entry = Entries.FirstOrDefault(e => e.Name == name) ?? throw new InvalidOperationException($"atalho “{name}” não existe mais");
        if (entry.Open is { } open)
        {
            var target = Environment.ExpandEnvironmentVariables(open);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            await Task.Delay(600);  // let it come to the front before typing or pressing keys
        }
        if (entry.Type is { } text) type(text);
        if (entry.Keys is { } keys)
        {
            foreach (var chord in keys.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries))
            {
                Win32.Chord(ParseChord(chord));
                await Task.Delay(60);
            }
        }
    }

    /// <summary>"ctrl+shift+s" → virtual keys.</summary>
    public static ushort[] ParseChord(string chord) =>
        chord.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(k => KeyCode(k.Trim().ToLowerInvariant())).ToArray();

    private static ushort KeyCode(string key) => key switch
    {
        "ctrl" or "control" => Win32.VK_CONTROL,
        "alt" => Win32.VK_MENU,
        "shift" => Win32.VK_SHIFT,
        "win" or "windows" => Win32.VK_LWIN,
        "enter" or "return" => Win32.VK_RETURN,
        "tab" => Win32.VK_TAB,
        "esc" or "escape" => Win32.VK_ESCAPE,
        "space" or "espaco" or "espaço" => Win32.VK_SPACE,
        "backspace" => Win32.VK_BACK,
        "delete" or "del" => Win32.VK_DELETE,
        "up" or "cima" => Win32.VK_UP,
        "down" or "baixo" => Win32.VK_DOWN,
        "left" or "esquerda" => Win32.VK_LEFT,
        "right" or "direita" => Win32.VK_RIGHT,
        "home" => Win32.VK_HOME,
        "end" => Win32.VK_END,
        "pageup" => Win32.VK_PRIOR,
        "pagedown" => Win32.VK_NEXT,
        "printscreen" or "print" => Win32.VK_SNAPSHOT,
        _ when key.Length > 1 && key[0] == 'f' && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24 => (ushort)(Win32.VK_F1 + f - 1),
        _ when key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]) => char.ToUpperInvariant(key[0]),
        _ => throw new InvalidOperationException($"tecla desconhecida “{key}”"),
    };

    private static List<string> Strings(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (!item.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return [value.GetString()!];
            if (value.ValueKind == JsonValueKind.Array)
                return value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).Where(s => s.Trim().Length > 0).ToList();
        }
        return [];
    }

    private static string? Text(JsonElement item, params string[] names) =>
        names.Select(n => item.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null).FirstOrDefault(s => s is not null);
}
