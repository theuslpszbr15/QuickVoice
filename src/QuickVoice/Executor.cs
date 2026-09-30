using System.Diagnostics;
using QuickVoice.Core;

namespace QuickVoice;

/// <summary>Runs commands on this PC: starts apps, opens sites, and types through simulated keyboard input.</summary>
internal sealed class Executor(InstalledApps apps, Shortcuts shortcuts, bool dryRun)
{
    // Calibration knob: time for a new note or document to take keyboard focus after Ctrl+N.
    private static readonly TimeSpan NewItemSettles = TimeSpan.FromMilliseconds(300);
    /// <summary>Opening these by voice could run a program: "abre o último arquivo baixado" never does.</summary>
    private static readonly HashSet<string> Runnable = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".com", ".scr", ".lnk", ".url", ".hta", ".reg", ".appref-ms" };

    /// <summary>The window the last command started, so "desfaz" can close it.</summary>
    private nint launched;

    /// <summary>Returns what went wrong, or null.</summary>
    public async Task<string?> RunAsync(Command command)
    {
        if (dryRun)
        {
            Terminal.Out($"   dry run: faria {command}\n");
            return null;
        }
        try
        {
            var justLaunched = launched;
            launched = 0;
            switch (command)
            {
                case Command.OpenApp open:
                    await OpenAsync(open.App);
                    break;
                case Command.NewItem:
                    PressCtrlN();
                    await Task.Delay(NewItemSettles);
                    break;
                case Command.TypeText type:
                    Type(type.Text);
                    break;
                case Command.Control { Action: SystemAction.Undo }:
                    if (justLaunched != 0 && Win32.IsWindow(justLaunched)) Win32.PostMessage(justLaunched, Win32.WM_CLOSE, 0, 0);
                    else Win32.Chord(Win32.VK_CONTROL, 'Z');
                    break;
                case Command.Control control:
                    if (control.App is { } app) await OpenAsync(app);  // "chrome na esquerda": that window, not the one in front
                    await ControlAsync(control);
                    break;
                case Command.CloseApp close:
                    CloseApp(close.App);
                    break;
                case Command.OpenFolder folder:
                    Explore(FolderPath(folder.Folder) ?? throw new InvalidOperationException($"não achei a pasta {folder.Folder}"));
                    break;
                case Command.OpenRecent recent:
                    var file = NewestFile(recent.Kind) ?? throw new InvalidOperationException($"nenhum {Recent.Describe(recent.Kind)} recente");
                    Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
                    break;
                case Command.Answer { Kind: "math" } answer:
                    System.Windows.Clipboard.SetText(answer.Value);  // ready to paste
                    break;
                case Command.Answer:
                    break;
                case Command.AgentTask agent:
                    await Agent.StartAsync(agent.Task, Settings.Load());  // read now: the agent screen may have changed it
                    break;
                case Command.Click click:
                    await Clicker.ClickAsync(Foreground.Window, click.Target);
                    break;
                case Command.Shortcut shortcut:
                    await shortcuts.RunAsync(shortcut.Name, Type, OpenByNameAsync);
                    break;
                default:
                    if (command.WebUrl is { } url) Browse(url);
                    break;
            }
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException
                                          or System.Runtime.InteropServices.COMException or System.Windows.Automation.ElementNotAvailableException
                                          or System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException)
        {
            Terminal.Out($"⚠ {command}: {error.Message}\n");
            return error.Message;
        }
    }

    private async Task OpenAsync(string name)
    {
        if (!apps.Targets.TryGetValue(name, out var target)) throw new InvalidOperationException($"nenhum app instalado chamado {name}");
        // Already open: bring that window forward instead of starting a second one.
        if (AppWindows.Find(name, target) is var open and not 0 && Win32.Activate(open)) return;
        var before = Foreground.Window;
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add($@"shell:AppsFolder\{target}");
        Process.Start(start)?.Dispose();
        for (var i = 0; i < 40; i++)  // up to 2 s, so the next keystrokes land in it
        {
            var now = Foreground.Window;
            if (now != 0 && now != before)
            {
                launched = now;
                return;
            }
            await Task.Delay(50);
        }
    }

    /// <summary>A routine's "Bloco de notas" or "teams": the installed app with that name or spoken name.</summary>
    private async Task<bool> OpenByNameAsync(string said)
    {
        var wanted = Normalize(said);
        var name = apps.Targets.Keys.FirstOrDefault(n => Normalize(n) == wanted)
                   ?? apps.Aliases.FirstOrDefault(a => a.Value.Any(alias => Normalize(alias) == wanted)).Key;
        if (name is null) return false;
        await OpenAsync(name);
        return true;
    }

    private void CloseApp(string name)
    {
        if (!apps.Targets.TryGetValue(name, out var target)) throw new InvalidOperationException($"nenhum app instalado chamado {name}");
        var windows = AppWindows.FindAll(name, target);
        if (windows.Count == 0) throw new InvalidOperationException($"{name} não está aberto");
        foreach (var window in windows) Win32.PostMessage(window, Win32.WM_CLOSE, 0, 0);  // asks, like the X: unsaved work still prompts
    }

    private static void Explore(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add(path);
        Process.Start(start)?.Dispose();
    }

    private static string? Downloads() => Win32.KnownFolder(new Guid("374DE290-123F-4565-9164-39C4925E467B"));

    /// <summary>A folder everyone has, or one of the user's own folders by name ("projetos" finds Documentos\Projetos).</summary>
    private static string? FolderPath(string folder)
    {
        string Special(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var known = folder switch
        {
            "downloads" => Downloads(),
            "documents" => Special(Environment.SpecialFolder.MyDocuments),
            "pictures" => Special(Environment.SpecialFolder.MyPictures),
            "music" => Special(Environment.SpecialFolder.MyMusic),
            "videos" => Special(Environment.SpecialFolder.MyVideos),
            "desktop" => Special(Environment.SpecialFolder.DesktopDirectory),
            _ => null,
        };
        if (known is not null) return Directory.Exists(known) ? known : null;
        var wanted = Normalize(folder);
        var roots = new[]
        {
            Special(Environment.SpecialFolder.UserProfile), Special(Environment.SpecialFolder.DesktopDirectory),
            Special(Environment.SpecialFolder.MyDocuments), Downloads(), Environment.GetEnvironmentVariable("OneDrive"),
        }.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var candidates = roots.SelectMany(r => Safe(() => Directory.EnumerateDirectories(r!))).ToList();
        return candidates.FirstOrDefault(d => Normalize(Path.GetFileName(d)) == wanted)
               ?? candidates.FirstOrDefault(d => wanted.Length >= 3 && Normalize(Path.GetFileName(d)).Contains(wanted));
    }

    /// <summary>The newest file of a kind in Downloads, the desktop, Documents and the files Windows lists as recent.</summary>
    private static string? NewestFile(string kind)
    {
        var extensions = Recent.Extensions.GetValueOrDefault(kind) ?? [];
        var recentLinks = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        var folders = kind == "download"
            ? new[] { Downloads() }
            : new[] { Downloads(), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), recentLinks };
        var files = folders.Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f))
            .SelectMany(f => Safe(() => new DirectoryInfo(f!).EnumerateFiles()))
            .Where(f => !f.Attributes.HasFlag(FileAttributes.Hidden))
            .Select(f =>
            {
                // Recent holds shortcuts named "relatório.pdf.lnk": what they point to decides the kind.
                var isLink = string.Equals(f.DirectoryName, recentLinks, StringComparison.OrdinalIgnoreCase) && f.Extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
                return (File: f, Extension: Path.GetExtension(isLink ? Path.GetFileNameWithoutExtension(f.Name) : f.Name));
            })
            .Where(f => f.Extension.Length > 0 && !Runnable.Contains(f.Extension) && !f.Extension.Equals(".ini", StringComparison.OrdinalIgnoreCase)
                        && !f.Extension.Equals(".crdownload", StringComparison.OrdinalIgnoreCase) && !f.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            .Where(f => extensions.Length == 0 || extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase));
        return files.OrderByDescending(f => f.File.LastWriteTimeUtc).Select(f => f.File.FullName).FirstOrDefault();
    }

    private static IEnumerable<T> Safe<T>(Func<IEnumerable<T>> list)
    {
        try
        {
            return list().ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string Normalize(string text) =>
        string.Join(' ', Vocabulary.Words(text).Select(Vocabulary.Normalized).Where(w => w.Length > 0));

    /// <summary>Opens in the browser in front ("abre o chrome e pesquisa…"), else in the default browser.</summary>
    private void Browse(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException($"endereço recusado: {url}");
        var front = Foreground.ExePath();
        if (front is not null && apps.BrowserExes.Contains(front))
        {
            var start = new ProcessStartInfo(front) { UseShellExecute = false };
            start.ArgumentList.Add(url.AbsoluteUri);
            Process.Start(start)?.Dispose();
        }
        else
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
    }

    /// <summary>Unicode units type any character in any layout; a new line is the Enter key, which every app understands.</summary>
    private static void Type(string text)
    {
        var inputs = new List<Native.INPUT>();
        foreach (var unit in text.Replace("\r\n", "\n"))
        {
            if (unit is '\n' or '\r')
            {
                Native.Key(inputs, Win32.VK_RETURN, 0, 0);
                Native.Key(inputs, Win32.VK_RETURN, 0, Native.KEYEVENTF_KEYUP);
                continue;
            }
            Native.Key(inputs, 0, unit, Native.KEYEVENTF_UNICODE);
            Native.Key(inputs, 0, unit, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP);
        }
        Native.Send(inputs);
    }

    private static async Task ControlAsync(Command.Control control)
    {
        var window = Foreground.Window;
        switch (control.Action)
        {
            case SystemAction.CloseWindow: Win32.Chord(Win32.VK_MENU, 0x73); break;  // Alt+F4
            case SystemAction.CloseTab: Win32.Chord(Win32.VK_CONTROL, 'W'); break;
            case SystemAction.Minimize: Win32.ShowWindow(window, Win32.SW_MINIMIZE); break;
            case SystemAction.Maximize: Win32.ShowWindow(window, Win32.SW_MAXIMIZE); break;
            case SystemAction.ShowDesktop: Win32.Chord(Win32.VK_LWIN, 'D'); break;
            case SystemAction.SwitchWindow: Win32.Chord(Win32.VK_MENU, Win32.VK_TAB); break;
            case SystemAction.VolumeUp: for (var i = 0; i < 5; i++) Win32.Chord(Win32.VK_VOLUME_UP); break;  // 5 steps of 2%
            case SystemAction.VolumeDown: for (var i = 0; i < 5; i++) Win32.Chord(Win32.VK_VOLUME_DOWN); break;
            case SystemAction.VolumeSet: SystemVolume.Set(control.Value ?? 50); break;
            case SystemAction.Mute: Win32.Chord(Win32.VK_VOLUME_MUTE); break;
            case SystemAction.PlayPause: Win32.Chord(Win32.VK_MEDIA_PLAY_PAUSE); break;
            case SystemAction.NextTrack: Win32.Chord(Win32.VK_MEDIA_NEXT); break;
            case SystemAction.PreviousTrack: Win32.Chord(Win32.VK_MEDIA_PREV); break;
            case SystemAction.Screenshot: Win32.Chord(Win32.VK_LWIN, Win32.VK_SNAPSHOT); break;  // saved to Imagens\Capturas de Tela
            case SystemAction.Lock: Win32.LockWorkStation(); break;
            case SystemAction.SnapLeft: Win32.Chord(Win32.VK_LWIN, Win32.VK_LEFT); break;
            case SystemAction.SnapRight: Win32.Chord(Win32.VK_LWIN, Win32.VK_RIGHT); break;
            case SystemAction.OtherMonitor: Win32.Chord(Win32.VK_LWIN, Win32.VK_SHIFT, Win32.VK_RIGHT); break;
        }
        await Task.Delay(150);  // the window or sound settles before the next command
    }

    /// <summary>Ctrl+N by virtual key, so it is the N of any keyboard layout.</summary>
    private static void PressCtrlN()
    {
        var inputs = new List<Native.INPUT>();
        Native.Key(inputs, (ushort)Native.VK_CONTROL, 0, 0);
        Native.Key(inputs, (ushort)Native.VK_N, 0, 0);
        Native.Key(inputs, (ushort)Native.VK_N, 0, Native.KEYEVENTF_KEYUP);
        Native.Key(inputs, (ushort)Native.VK_CONTROL, 0, Native.KEYEVENTF_KEYUP);
        Native.Send(inputs);
    }
}
