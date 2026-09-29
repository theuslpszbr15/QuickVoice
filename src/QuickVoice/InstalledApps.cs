using Microsoft.Win32;

namespace QuickVoice;

/// <summary>
/// Everything in the Start menu (Win32 shortcuts and Store apps alike), from the shell's own "Apps" folder:
/// names come already in the language of Windows ("Bloco de Notas"), and each one knows how to start itself.
/// </summary>
internal sealed class InstalledApps
{
    /// <summary>App name → shell parsing name (an AppUserModelID or a path), launched as shell:AppsFolder\&lt;target&gt;.</summary>
    public IReadOnlyDictionary<string, string> Targets { get; }
    public IReadOnlyList<string> Names { get; }
    public IReadOnlyDictionary<string, List<string>> Aliases { get; }
    /// <summary>The installed apps that are web browsers, e.g. "Google Chrome", "Microsoft Edge".</summary>
    public IReadOnlySet<string> Browsers { get; }
    /// <summary>Browser executables, to open a site in the one in front.</summary>
    public IReadOnlySet<string> BrowserExes { get; }

    private static readonly string[] NotApps =
    [
        "uninstall", "desinstalar", "remover", "readme", "read me", "leia-me", "help", "ajuda", "documentation", "documentação",
        "manual", "license", "licença", "release notes", "website", "site da web", "changelog",
    ];

    private static readonly string[] DocumentExtensions = [".url", ".txt", ".chm", ".pdf", ".htm", ".html", ".rtf", ".md", ".ini", ".log"];

    private InstalledApps(Dictionary<string, string> targets, HashSet<string> browsers, HashSet<string> browserExes)
    {
        Targets = targets;
        Names = [.. targets.Keys.Order(StringComparer.OrdinalIgnoreCase)];
        Aliases = targets.Keys.ToDictionary(name => name, name => new List<string> { name });
        Browsers = browsers;
        BrowserExes = browserExes;
    }

    public static InstalledApps Load()
    {
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var shellType = Type.GetTypeFromProgID("Shell.Application") ?? throw new InvalidOperationException("Shell.Application indisponível");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");
        foreach (dynamic item in folder.Items())
        {
            string name = ((string)item.Name).Trim();
            string target = item.Path;
            if (name.Length == 0 || string.IsNullOrEmpty(target) || !LooksLikeApp(name, target)) continue;
            targets.TryAdd(name, target);  // first entry wins
        }
        var (browserNames, browserExes) = RegisteredBrowsers();
        var browsers = targets.Keys
            .Where(app => browserNames.Any(b => app.Equals(b, StringComparison.OrdinalIgnoreCase)
                                                || app.StartsWith(b, StringComparison.OrdinalIgnoreCase)
                                                || b.StartsWith(app, StringComparison.OrdinalIgnoreCase)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new InstalledApps(targets, browsers, browserExes);
    }

    private static bool LooksLikeApp(string name, string target)
    {
        if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        if (DocumentExtensions.Any(ext => target.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return false;
        return !NotApps.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Browsers as Windows registers them for "Default apps": their display names and executables.</summary>
    private static (List<string> Names, HashSet<string> Exes) RegisteredBrowsers()
    {
        var names = new List<string>();
        var exes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var path in new[] { @"SOFTWARE\Clients\StartMenuInternet", @"SOFTWARE\WOW6432Node\Clients\StartMenuInternet" })
            {
                using var clients = root.OpenSubKey(path);
                if (clients is null) continue;
                foreach (var id in clients.GetSubKeyNames())
                {
                    using var client = clients.OpenSubKey(id);
                    if (client?.GetValue(null) is string name && name.Length > 0) names.Add(name);
                    using var command = client?.OpenSubKey(@"shell\open\command");
                    if (command?.GetValue(null) is string line && ExeOf(line) is { } exe) exes.Add(exe);
                }
            }
        }
        return (names, exes);
    }

    private static string? ExeOf(string commandLine)
    {
        var line = commandLine.Trim();
        var exe = line.StartsWith('"') ? line[1..Math.Max(1, line.IndexOf('"', 1))] : line.Split(' ')[0];
        return exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(exe) : null;
    }
}
