using System.Windows.Interop;

namespace QuickVoice;

/// <summary>A system-wide hotkey: the first of its candidates that no other app holds.</summary>
internal sealed class Hotkey : IDisposable
{
    /// <summary>Alt+Space like the original, else Ctrl+Alt+Space.</summary>
    public static readonly (uint Modifiers, string Label)[] Listen =
        [(Native.MOD_ALT, "Alt+Espaço"), (Native.MOD_CONTROL | Native.MOD_ALT, "Ctrl+Alt+Espaço")];

    public static readonly (uint Modifiers, string Label)[] Write =
        [(Native.MOD_ALT | Native.MOD_SHIFT, "Alt+Shift+Espaço"), (Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT, "Ctrl+Alt+Shift+Espaço")];

    /// <summary>The listen shortcut chosen in the settings; "auto" tries the defaults in order.</summary>
    public static (uint Modifiers, string Label)[] ListenFor(string choice) => choice switch
    {
        "alt" => [Listen[0]],
        "ctrl-alt" => [Listen[1]],
        "ctrl-shift" => [(Native.MOD_CONTROL | Native.MOD_SHIFT, "Ctrl+Shift+Espaço")],
        _ => Listen,
    };

    private readonly nint hwnd;
    private readonly int id;
    private readonly Action pressed;
    private readonly HwndSource source;

    public string? Label { get; }

    public Hotkey(nint hwnd, int id, (uint Modifiers, string Label)[] candidates, Action pressed)
    {
        this.hwnd = hwnd;
        this.id = id;
        this.pressed = pressed;
        source = HwndSource.FromHwnd(hwnd);
        source.AddHook(Hook);
        foreach (var (modifiers, label) in candidates)
        {
            if (Native.RegisterHotKey(hwnd, id, modifiers | Native.MOD_NOREPEAT, Native.VK_SPACE))
            {
                Label = label;
                break;
            }
        }
    }

    private nint Hook(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == Native.WM_HOTKEY && wParam == id)
        {
            handled = true;
            pressed();
        }
        return 0;
    }

    public void Dispose()
    {
        if (Label is not null) Native.UnregisterHotKey(hwnd, id);
        source.RemoveHook(Hook);
    }
}
