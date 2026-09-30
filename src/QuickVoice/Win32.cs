using System.Runtime.InteropServices;
using System.Text;

namespace QuickVoice;

/// <summary>Window, keyboard chord and mouse calls the executor needs beyond <see cref="Native"/>.</summary>
internal static class Win32
{
    public const int SW_MINIMIZE = 6, SW_MAXIMIZE = 3, SW_RESTORE = 9;
    public const ushort VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12,
        VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24, VK_LEFT = 0x25,
        VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_SNAPSHOT = 0x2C, VK_DELETE = 0x2E, VK_LWIN = 0x5B, VK_F1 = 0x70,
        VK_VOLUME_MUTE = 0xAD, VK_VOLUME_DOWN = 0xAE, VK_VOLUME_UP = 0xAF, VK_MEDIA_NEXT = 0xB0, VK_MEDIA_PREV = 0xB1,
        VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x2, MOUSEEVENTF_LEFTUP = 0x4, INPUT_MOUSE = 0;
    private const int GW_OWNER = 4, DWMWA_CLOAKED = 14;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public delegate bool EnumProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, nint lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint parent, EnumProc proc, nint lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, int command);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
    public const uint WM_CLOSE = 0x0010;
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int max);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder id);
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    public static string Title(nint hwnd)
    {
        var text = new StringBuilder(512);
        return GetWindowText(hwnd, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    public static string ClassName(nint hwnd)
    {
        var text = new StringBuilder(256);
        return GetClassName(hwnd, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    /// <summary>A window the taskbar would show: visible, not owned, not hidden on another virtual desktop.</summary>
    public static bool IsAppWindow(nint hwnd) =>
        IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == 0 && Title(hwnd).Length > 0
        && (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) != 0 || cloaked == 0)
        && (Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TOOLWINDOW) == 0;

    public static uint ProcessId(nint hwnd)
    {
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    public static string? ExePath(uint pid) => WithProcess(pid, handle =>
    {
        var buffer = new char[1024];
        var size = (uint)buffer.Length;
        return Native.QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
    });

    /// <summary>The package app id of a Store or packaged process ("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App").</summary>
    public static string? ProcessAppId(uint pid) => WithProcess(pid, handle =>
    {
        uint length = 256;
        var id = new StringBuilder((int)length);
        return GetApplicationUserModelId(handle, ref length, id) == 0 ? id.ToString() : null;
    });

    /// <summary>The app id a window declares for itself (how Chrome, Office and the taskbar group windows).</summary>
    public static string? WindowAppId(nint hwnd)
    {
        var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store is null) return null;
        try
        {
            var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);  // PKEY_AppUserModel_ID
            if (store.GetValue(ref key, out var value) != 0) return null;
            try
            {
                return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null;  // VT_LPWSTR
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    /// <summary>"{GUID}\Notepad++\notepad++.exe" (how the Apps folder names classic apps) → a real path.</summary>
    public static string? KnownFolderPath(string target)
    {
        if (!target.StartsWith('{') || target.IndexOf('}') is not (> 0 and var close) || !Guid.TryParse(target[..(close + 1)], out var id))
            return target.Contains('\\') ? target : null;
        if (SHGetKnownFolderPath(ref id, 0, 0, out var path) != 0) return null;
        try
        {
            return Path.Join(Marshal.PtrToStringUni(path), target[(close + 1)..].TrimStart('\\'));
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    /// <summary>A known folder by id (Downloads has no Environment.SpecialFolder).</summary>
    public static string? KnownFolder(Guid id)
    {
        if (SHGetKnownFolderPath(ref id, 0, 0, out var path) != 0) return null;
        try
        {
            return Marshal.PtrToStringUni(path);
        }
        finally
        {
            Marshal.FreeCoTaskMem(path);
        }
    }

    /// <summary>Presses the keys in order, then releases them in reverse: Chord(VK_CONTROL, 'W').</summary>
    public static void Chord(params ushort[] keys)
    {
        var inputs = new List<Native.INPUT>();
        foreach (var key in keys) Native.Key(inputs, key, 0, 0);
        foreach (var key in keys.Reverse()) Native.Key(inputs, key, 0, Native.KEYEVENTF_KEYUP);
        Native.Send(inputs);
    }

    /// <summary>
    /// Windows only lets the app in front hand over focus. A tapped Alt counts as fresh input and lifts the lock;
    /// failing that, sharing the input queue of the window in front does; SwitchToThisWindow is the last resort.
    /// </summary>
    public static bool Activate(nint hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        Chord(VK_MENU);
        if (Native.SetForegroundWindow(hwnd) && Native.GetForegroundWindow() == hwnd) return true;
        var front = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var mine = GetCurrentThreadId();
        var attached = front != mine && AttachThreadInput(mine, front, true);
        try
        {
            BringWindowToTop(hwnd);
            Native.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(mine, front, false);
        }
        if (Native.GetForegroundWindow() != hwnd) SwitchToThisWindow(hwnd, true);
        return Native.GetForegroundWindow() == hwnd;
    }

    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool doAttach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32.dll")] private static extern void SwitchToThisWindow(nint hwnd, bool altTab);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    public static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        var down = new Native.INPUT { type = INPUT_MOUSE, u = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } } };
        var up = new Native.INPUT { type = INPUT_MOUSE, u = new Native.InputUnion { mi = new Native.MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } } };
        Native.Send([down, up]);
    }

    private static T? WithProcess<T>(uint pid, Func<nint, T?> read)
    {
        if (pid == 0) return default;
        var handle = Native.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0) return default;
        try
        {
            return read(handle);
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid format, uint id)
    {
        public Guid Format = format;
        public uint Id = id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public nint Pointer;
        public nint Extra;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
