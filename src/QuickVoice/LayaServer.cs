using System.ComponentModel;
using System.Diagnostics;

namespace QuickVoice;

/// <summary>
/// Laya (https://github.com/NandhaKishorM/laya), the open decision model, served on this PC by its own Python:
/// installed once into %LOCALAPPDATA%\QuickVoice\laya, started hidden with the app and stopped when it quits.
/// </summary>
internal static class LayaServer
{
    public const int Port = 8765;
    public static readonly Uri Endpoint = new($"http://127.0.0.1:{Port}/v1/systemone");

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickVoice", "laya");

    private static string Serve => Path.Combine(Folder, "venv", "Scripts", "laya-serve.exe");

    public static bool Installed => File.Exists(Serve);

    private static Process? server;

    /// <summary>Loads the model in the background (~30 s); until it answers, the local rules decide.</summary>
    public static void Start()
    {
        if (!Installed || server is { HasExited: false }) return;
        var running = Process.GetProcessesByName("laya-serve");
        var already = running.Length > 0;  // another QuickVoice, or one left behind
        foreach (var process in running) process.Dispose();
        if (already) return;
        var info = new ProcessStartInfo(Serve) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Folder };
        info.Environment["LAYA_HOST"] = "127.0.0.1";  // this PC only
        info.Environment["LAYA_PORT"] = Port.ToString();
        info.Environment["LAYA_MODELS"] = "multilingual";
        info.Environment["LAYA_DEFAULT_MODEL"] = "multilingual";
        info.Environment["LAYA_PRELOAD"] = "1";
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        try
        {
            server = Process.Start(info);
        }
        catch (Win32Exception)
        {
            server = null;
        }
    }

    public static void Stop()
    {
        try
        {
            server?.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            // already gone
        }
        server?.Dispose();
        server = null;
    }

    /// <summary>A visible terminal: Python venv, pip install "laya[serve]", then the multilingual checkpoint (~1.5 GB in all).</summary>
    public static void Install()
    {
        Directory.CreateDirectory(Folder);
        var script = Path.Combine(Folder, "install.cmd");
        File.WriteAllText(script, InstallScript.ReplaceLineEndings("\r\n"));  // cmd's goto needs CRLF
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"") { UseShellExecute = true })?.Dispose();
    }

    private const string InstallScript = """
        @echo off
        title QuickVoice - instalando a Laya
        cd /d "%~dp0"
        echo.
        echo  Instalando a Laya: a IA de decisoes que roda no seu PC (gratis, offline).
        echo  Baixa cerca de 1,5 GB (PyTorch + modelo multilingual). Pode levar alguns minutos.
        echo.
        set "PY=py -3"
        where py >nul 2>nul || set "PY=python"
        if not exist "venv\Scripts\python.exe" (
          %PY% -m venv venv || goto :fail
        )
        "venv\Scripts\python.exe" -m pip install --upgrade pip || goto :fail
        "venv\Scripts\python.exe" -m pip install "laya[serve]" || goto :fail
        echo.
        echo  Baixando o modelo multilingual (cerca de 650 MB)...
        "venv\Scripts\python.exe" -c "from laya import Router; Router().predict('abre o spotify', {'q': {'type': 'noul', 'instructions': 'Pede para abrir um app?'}}, model='multilingual')" || goto :fail
        echo.
        echo  Pronto! Em Configuracoes do QuickVoice, escolha "Laya" em "Quem decide" e salve.
        pause
        exit /b 0
        :fail
        echo.
        echo  A instalacao falhou. Precisa do Python 3.10 ou mais novo (python.org) e de internet.
        pause
        exit /b 1

        """;
}
