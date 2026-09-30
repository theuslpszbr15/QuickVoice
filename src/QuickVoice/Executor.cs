using System.Diagnostics;
using QuickVoice.Core;

namespace QuickVoice;

/// <summary>Runs commands on this PC: starts apps, opens sites, and types through simulated keyboard input.</summary>
internal sealed class Executor(InstalledApps apps, Shortcuts shortcuts, bool dryRun)
{
    // Calibration knob: time for a new note or document to take keyboard focus after Ctrl+N.
    private static readonly TimeSpan NewItemSettles = TimeSpan.FromMilliseconds(300);

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
                case Command.Control control:
                    await ControlAsync(control);
                    break;
                case Command.Click click:
                    await Clicker.ClickAsync(Foreground.Window, click.Target);
                    break;
                case Command.Shortcut shortcut:
                    await shortcuts.RunAsync(shortcut.Name, Type);
                    break;
                default:
                    if (command.WebUrl is { } url) Browse(url);
                    break;
            }
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException
                                          or System.Runtime.InteropServices.COMException or System.Windows.Automation.ElementNotAvailableException)
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
            if (now != 0 && now != before) return;
            await Task.Delay(50);
        }
    }

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
