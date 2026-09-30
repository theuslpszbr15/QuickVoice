using System.Diagnostics;
using QuickVoice.Core;

namespace QuickVoice;

/// <summary>Finds a window an installed app already has open, so "abre o chrome" brings it forward instead of a new one.</summary>
internal static class AppWindows
{
    /// <summary>
    /// The frontmost matching window, trying the surest signal first: the app id Windows groups it by, then the
    /// executable, then what the program calls itself, then the window title ("Sem título – Bloco de Notas").
    /// </summary>
    public static nint Find(string name, string target) => FindAll(name, target).FirstOrDefault();

    /// <summary>Every window of the app, surest matches first (all of one strength: "fecha o chrome" closes every Chrome window).</summary>
    public static List<nint> FindAll(string name, string target)
    {
        var path = Win32.KnownFolderPath(target);
        var isAppId = target.Contains('!') || (!target.Contains('\\') && path is null);
        var wanted = Normalize(name);
        var matches = new List<(nint Window, int Strength)>();
        var own = (uint)Environment.ProcessId;

        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsAppWindow(hwnd)) return true;
            var pid = HostedProcess(hwnd);
            if (pid == own) return true;
            var strength = 0;
            if (isAppId && (Same(Win32.WindowAppId(hwnd), target) || Same(Win32.ProcessAppId(pid), target))) strength = 4;
            else if (Win32.ExePath(pid) is { } exe)
            {
                if (path is not null && Same(exe, path)) strength = 3;
                else if (Describes(exe, wanted)) strength = 2;
            }
            if (strength == 0 && TitleNames(Win32.Title(hwnd), wanted)) strength = 1;
            if (strength > 0) matches.Add((hwnd, strength));
            return true;
        }, 0);

        if (matches.Count == 0) return [];
        var best = matches.Max(m => m.Strength);
        return matches.Where(m => m.Strength == best).Select(m => m.Window).ToList();  // z-order: the frontmost first
    }

    /// <summary>Store apps draw inside ApplicationFrameHost: the app's own process owns a child window.</summary>
    private static uint HostedProcess(nint hwnd)
    {
        var pid = Win32.ProcessId(hwnd);
        if (Win32.ClassName(hwnd) != "ApplicationFrameWindow") return pid;
        var hosted = pid;
        Win32.EnumChildWindows(hwnd, (child, _) =>
        {
            var childPid = Win32.ProcessId(child);
            if (childPid == pid) return true;
            hosted = childPid;
            return false;
        }, 0);
        return hosted;
    }

    private static bool Describes(string exe, string wanted)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            return new[] { info.FileDescription, info.ProductName, Path.GetFileNameWithoutExtension(exe) }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => Normalize(s!))
                .Any(s => s == wanted || (wanted.Length >= 4 && s.Length >= 4 && (s.Contains(wanted) || wanted.Contains(s))));
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static bool TitleNames(string title, string wanted) =>
        Normalize(title.Split([" - ", " \u2013 ", " \u2014 "], StringSplitOptions.None)[^1]) == wanted;

    private static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string text) =>
        string.Join(' ', Vocabulary.Words(text).Select(Vocabulary.Normalized).Where(w => w.Length > 0));
}
