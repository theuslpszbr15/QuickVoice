using System.Diagnostics;
using QuickVoice.Core;

namespace QuickVoice;

/// <summary>Runs commands on this PC: starts apps, opens sites, and types through simulated keyboard input.</summary>
internal sealed class Executor(InstalledApps apps, bool dryRun)
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
                default:
                    if (command.WebUrl is { } url) Browse(url);
                    break;
            }
            return null;
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            Terminal.Out($"⚠ {command}: {error.Message}\n");
            return error.Message;
        }
    }

    private async Task OpenAsync(string name)
    {
        if (!apps.Targets.TryGetValue(name, out var target)) throw new InvalidOperationException($"nenhum app instalado chamado {name}");
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

    private static void Type(string text)
    {
        var inputs = new List<Native.INPUT>();
        foreach (var unit in text)
        {
            Native.Key(inputs, 0, unit, Native.KEYEVENTF_UNICODE);
            Native.Key(inputs, 0, unit, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP);
        }
        Native.Send(inputs);
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
