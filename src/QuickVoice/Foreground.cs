using System.Diagnostics;

namespace QuickVoice;

/// <summary>The window in front: what Jev is told, and whether a site should open in it.</summary>
internal static class Foreground
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public static nint Window => Native.GetForegroundWindow();

    /// <summary>The foreground process's executable, or null when Windows will not say (elevated, protected).</summary>
    public static string? ExePath()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
        if (pid == 0) return null;
        var handle = Native.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0) return null;
        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    /// <summary>A human name for the app in front: "Google Chrome", "Bloco de Notas"...</summary>
    public static string? AppName()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == 0) return null;
        var path = ExePath();
        // Store apps are hosted by ApplicationFrameHost: their window title names them better.
        if (path is not null && !path.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch (FileNotFoundException) { }
            return Path.GetFileNameWithoutExtension(path);
        }
        var title = new char[256];
        var length = Native.GetWindowText(hwnd, title, title.Length);
        return length > 0 ? new string(title, 0, length) : null;
    }
}
