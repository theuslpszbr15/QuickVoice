using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QuickVoice;

/// <summary>A plugin the agent may use: a local program (stdio) or a remote server (http URL).</summary>
internal sealed class McpServer
{
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    /// <summary>A program ("npx") or an https:// URL.</summary>
    public string Command { get; set; } = "";
    public string Args { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsRemote => Command.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || Command.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public static McpServer Playwright() => new() { Name = "playwright", Command = "npx", Args = "-y @playwright/mcp@latest" };
    public static McpServer Files(string folder) => new() { Name = "arquivos", Command = "npx", Args = $"-y @modelcontextprotocol/server-filesystem \"{folder}\"" };
    public static McpServer Fetch() => new() { Name = "fetch", Command = "uvx", Args = "mcp-server-fetch" };

    /// <summary>"-y \"C:\\Meus Projetos\" --flag" → ["-y", "C:\\Meus Projetos", "--flag"].</summary>
    public IEnumerable<string> ArgList() =>
        Regex.Matches(Args, "\"([^\"]*)\"|(\\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
}

/// <summary>A model the Copilot CLI can run, with how fast it tends to answer (1 fastest … 5 slowest).</summary>
internal sealed record AgentModel(string Id, int Speed)
{
    public string SpeedLabel => Speed switch
    {
        0 => "o Copilot escolhe",
        1 => "mais rápido",
        2 => "rápido",
        3 => "equilibrado",
        4 => "mais capaz",
        _ => "mais lento, o mais capaz",
    };

    public override string ToString() => Id == "auto" ? "Automático" : Id;
}

/// <summary>
/// Hands a task to GitHub Copilot, signed in with the user's own Copilot account: the Copilot CLI in a terminal
/// (any model), or the Copilot chat of VS Code in agent mode. Each task gets its own project folder, with the
/// chosen MCP servers configured for it.
/// </summary>
internal static class Agent
{
    private static readonly string NpmFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
    private static string CliLoader => Path.Combine(NpmFolder, "node_modules", "@github", "copilot", "npm-loader.js");
    private static string McpConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickVoice", "mcp-config.json");

    /// <summary>Used when the CLI cannot list its models (not installed yet).</summary>
    private static readonly string[] KnownModels =
        ["claude-haiku-4.5", "gpt-5-mini", "gpt-5.4-mini", "gemini-3.8-flash", "claude-sonnet-4.6", "claude-sonnet-5", "gpt-5.5", "gpt-5.3-codex", "claude-opus-5", "claude-opus-5.5"];

    public static bool CliInstalled => File.Exists(CliLoader) && Node() is not null;

    public static string? Node()
    {
        var candidates = new[]
        {
            Path.Combine(NpmFolder, "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
        }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => Path.Combine(p.Trim(), "node.exe")));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>The Copilot CLI's version line, or null.</summary>
    public static async Task<string?> CliVersionAsync() =>
        CliInstalled ? (await CliOutputAsync("--version"))?.Split('\n').FirstOrDefault()?.Trim() : null;

    /// <summary>The models this CLI knows, fastest first ("auto" on top).</summary>
    public static async Task<List<AgentModel>> ModelsAsync()
    {
        var ids = new List<string>();
        if (CliInstalled && await CliOutputAsync("help", "config") is { } help)
        {
            var lines = help.Split('\n');
            var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith("`model`", StringComparison.Ordinal));
            if (start >= 0)
            {
                ids = lines.Skip(start + 1).TakeWhile(l => l.TrimStart().StartsWith("- \"", StringComparison.Ordinal))
                    .Select(l => l.Trim().TrimStart('-', ' ').Trim('"')).Where(id => id.Length > 0).ToList();
            }
        }
        if (ids.Count == 0) ids = [.. KnownModels];
        return [new AgentModel("auto", 0), .. ids.Distinct().Select(id => new AgentModel(id, Speed(id))).OrderBy(m => m.Speed).ThenBy(m => m.Id, StringComparer.Ordinal)];
    }

    /// <summary>An estimate from the name: small tiers answer first, the largest reason longest.</summary>
    public static int Speed(string id)
    {
        var name = id.ToLowerInvariant();
        bool Has(params string[] parts) => parts.Any(name.Contains);
        if (Has("flash", "mini", "haiku", "nano", "lite")) return 1;
        if (Has("opus") && Has("fast")) return 3;
        if (Has("opus", "-sol", "-max", "ultra")) return 5;
        if (Has("luna", "fast")) return 2;
        if (Has("codex", "astra", "fable", "-pro")) return 4;
        return 3;
    }

    /// <summary>Opens a terminal to install the Copilot CLI with npm.</summary>
    public static void InstallCli() => InTerminal("cmd.exe", ["/k", "npm install -g @github/copilot && echo. && echo Pronto. Agora use \"Entrar na conta\" no QuickVoice."]);

    /// <summary>Opens the Copilot CLI's sign-in (device code in the browser) in a terminal.</summary>
    public static void Login()
    {
        if (Node() is { } node && File.Exists(CliLoader)) InTerminal(node, [CliLoader, "login"]);
    }

    /// <summary>
    /// Starts <paramref name="task"/> in a new project folder and says where. The words go to the agent as
    /// arguments, never through a shell, so nothing said can become a command of its own.
    /// </summary>
    public static async Task<string> StartAsync(string task, Settings settings)
    {
        var folder = ProjectFolder(settings.AgentFolder, task);
        Directory.CreateDirectory(folder);
        WriteInstructions(folder, task);
        var servers = settings.McpServers.Where(s => s.Enabled && s.Name.Trim().Length > 0 && s.Command.Trim().Length > 0).ToList();
        WriteVsCodeMcp(folder, servers);

        if (settings.AgentHarness == "vscode" || !CliInstalled)
        {
            var code = VsCode() ?? throw new InvalidOperationException("instale o Copilot CLI ou o VS Code para usar o agente");
            RunCode(code, ["-n", folder]);
            await WaitForWindowAsync(Path.GetFileName(folder), TimeSpan.FromSeconds(20));
            RunCode(code, ["chat", "-r", "-m", "agent", task]);
            return $"Copilot (VS Code) trabalhando em {folder}";
        }

        var config = WriteCliMcp(servers);
        var args = new List<string> { CliLoader };
        if (settings.AgentModel is { Length: > 0 } model && model != "auto") args.AddRange(["--model", model]);
        if (config is not null) args.AddRange(["--additional-mcp-config", "@" + config]);
        if (settings.AgentAutonomous) args.Add("--allow-all-tools");
        args.AddRange(["-i", task]);
        InTerminal(Node()!, args, folder);
        if (settings.AgentOpensVsCode && VsCode() is { } editor) RunCode(editor, ["-n", folder]);
        return $"Copilot ({(settings.AgentModel is "auto" or "" ? "modelo automático" : settings.AgentModel)}) trabalhando em {folder}";
    }

    private static string ProjectFolder(string root, string task)
    {
        var slug = string.Join('-', Core.Vocabulary.Words(task).Select(Core.Vocabulary.Normalized)
            .Where(w => w.Length > 2 && w.All(char.IsAsciiLetterOrDigit)).Take(4));
        if (slug.Length == 0) slug = "tarefa";
        var baseRoot = string.IsNullOrWhiteSpace(root) ? new Settings().AgentFolder : Environment.ExpandEnvironmentVariables(root);
        return Path.Combine(baseRoot, $"{slug}-{DateTime.Now:yyyyMMdd-HHmm}");
    }

    private static void WriteInstructions(string folder, string task)
    {
        var github = Path.Combine(folder, ".github");
        Directory.CreateDirectory(github);
        File.WriteAllText(Path.Combine(github, "copilot-instructions.md"), $"""
            # Pedido feito por voz no QuickVoice

            > {task}

            - Entregue um projeto completo e funcionando nesta pasta, não um esboço.
            - Prefira tecnologias simples de rodar no Windows e explique no README.md como abrir ou executar.
            - Se o pedido for ambíguo, escolha o caminho mais útil e registre a decisão no README.md.
            - Teste o que for possível antes de terminar.
            """, Encoding.UTF8);
    }

    private static void WriteVsCodeMcp(string folder, List<McpServer> servers)
    {
        var entries = new JsonObject();
        foreach (var s in servers)
            entries[s.Name] = s.IsRemote
                ? new JsonObject { ["type"] = "http", ["url"] = s.Command }
                : new JsonObject { ["type"] = "stdio", ["command"] = s.Command, ["args"] = new JsonArray([.. s.ArgList().Select(a => (JsonNode)a)]) };
        var vscode = Path.Combine(folder, ".vscode");
        Directory.CreateDirectory(vscode);
        File.WriteAllText(Path.Combine(vscode, "mcp.json"), new JsonObject { ["servers"] = entries }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? WriteCliMcp(List<McpServer> servers)
    {
        if (servers.Count == 0) return null;
        var entries = new JsonObject();
        foreach (var s in servers)
            entries[s.Name] = s.IsRemote
                ? new JsonObject { ["type"] = "http", ["url"] = s.Command, ["tools"] = new JsonArray("*") }
                : new JsonObject { ["type"] = "local", ["command"] = s.Command, ["args"] = new JsonArray([.. s.ArgList().Select(a => (JsonNode)a)]), ["tools"] = new JsonArray("*") };
        Directory.CreateDirectory(Path.GetDirectoryName(McpConfigPath)!);
        File.WriteAllText(McpConfigPath, new JsonObject { ["mcpServers"] = entries }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return McpConfigPath;
    }

    /// <summary>VS Code's Code.exe and its cli.js, read from code.cmd (the folder name changes with each update).</summary>
    private static (string Exe, string Cli)? VsCode()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code"),
        };
        foreach (var root in roots)
        {
            var cmd = Path.Combine(root, "bin", "code.cmd");
            if (!File.Exists(cmd)) continue;
            var cli = Regex.Match(File.ReadAllText(cmd), @"%~dp0\.\.\\([^""]*cli\.js)") is { Success: true } m ? Path.Combine(root, m.Groups[1].Value) : null;
            var exe = Path.Combine(root, "Code.exe");
            if (cli is not null && File.Exists(cli) && File.Exists(exe)) return (exe, cli);
        }
        return null;
    }

    private static void RunCode((string Exe, string Cli) code, IEnumerable<string> args)
    {
        var start = new ProcessStartInfo(code.Exe) { UseShellExecute = false, CreateNoWindow = true };
        start.Environment["ELECTRON_RUN_AS_NODE"] = "1";
        start.ArgumentList.Add(code.Cli);
        foreach (var a in args) start.ArgumentList.Add(a);
        Process.Start(start)?.Dispose();
    }

    private static async Task WaitForWindowAsync(string titlePart, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var found = false;
            Win32.EnumWindows((hwnd, _) =>
            {
                if (Win32.IsAppWindow(hwnd) && Win32.Title(hwnd).Contains(titlePart, StringComparison.OrdinalIgnoreCase)) found = true;
                return !found;
            }, 0);
            if (found)
            {
                await Task.Delay(2500);  // the window is up; its Copilot chat needs a moment more
                return;
            }
            await Task.Delay(300);
        }
    }

    /// <summary>A program in a console window of its own (QuickVoice has none).</summary>
    private static void InTerminal(string exe, IEnumerable<string> args, string? folder = null)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        foreach (var a in args) start.ArgumentList.Add(a);
        Process.Start(start)?.Dispose();
    }

    /// <summary>
    /// Whether this account may use <paramref name="model"/>. The CLI only checks it when it has a prompt, so this sends
    /// a tiny one (a small request on the account) and only when the user asks.
    /// </summary>
    public static async Task<(bool Ok, string Message)> CheckModelAsync(string model)
    {
        if (!CliInstalled) return (false, "Copilot CLI não instalado");
        string[] args = model == "auto" ? ["-p", "responda apenas: ok", "-s"] : ["--model", model, "-p", "responda apenas: ok", "-s"];
        var output = (await CliOutputAsync(TimeSpan.FromSeconds(90), args))?.Trim() ?? "sem resposta";
        return output.Contains("not available", StringComparison.OrdinalIgnoreCase) || output.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
            ? (false, output.Contains("not available", StringComparison.OrdinalIgnoreCase) ? "não liberado na sua conta: use Automático ou outro" : output)
            : (true, "disponível e respondendo");
    }

    private static Task<string?> CliOutputAsync(params string[] args) => CliOutputAsync(TimeSpan.FromSeconds(20), args);

    private static async Task<string?> CliOutputAsync(TimeSpan limit, params string[] args)
    {
        try
        {
            var start = new ProcessStartInfo(Node()!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(CliLoader);
            foreach (var a in args) start.ArgumentList.Add(a);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(limit);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            return await output + await errors;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
