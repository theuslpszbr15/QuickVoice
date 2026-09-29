namespace QuickVoice;

/// <summary>Console output for the terminal modes; a no-op when the app runs with no console.</summary>
internal static class Terminal
{
    private const int ATTACH_PARENT_PROCESS = -1;
    private const int STD_OUTPUT_HANDLE = -11;

    /// <summary>A windowed app has no console: borrow the one it was started from, unless output is redirected.</summary>
    public static void Attach()
    {
        if (Native.GetStdHandle(STD_OUTPUT_HANDLE) == 0) Native.AttachConsole(ATTACH_PARENT_PROCESS);
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { }
    }

    public static void Out(string text)
    {
        try
        {
            Console.Write(text);
            Console.Out.Flush();
        }
        catch (IOException) { }
    }
}
