using System.Windows;
using System.Windows.Threading;
using QuickVoice.Core;

namespace QuickVoice;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ArgumentException error)
        {
            Terminal.Attach();
            Console.Error.WriteLine($"{error.Message}\n\n{Options.Usage}");
            return 2;
        }
        if (options.Help || options.Text is not null) Terminal.Attach();
        if (options.Help)
        {
            Terminal.Out(Options.Usage + "\n");
            return 0;
        }

        using var single = new Mutex(true, @"Local\QuickVoice", out var first);
        if (!first && options.Restarted)
        {
            try
            {
                first = single.WaitOne(TimeSpan.FromSeconds(10));
            }
            catch (AbandonedMutexException)
            {
                first = true;
            }
        }
        if (!first && options.Text is null) return 0;  // the bar is already up

        var key = ApiKey.Load();
        var apps = InstalledApps.Load();
        var settings = Settings.Load();
        var shortcuts = new Shortcuts();
        var log = options.Log ? new EventLog() : null;
        if (log is not null) Terminal.Out($"📝 gravando em {log.Path}\n");
        log?.Write("start", new() { ["mode"] = options.Text is null ? "app" : "text", ["dry_run"] = options.DryRun, ["apps"] = apps.Names.Count });
        var session = new Session(key is null ? new LocalDecider() : new JevClient(key), apps, new Executor(apps, shortcuts, options.DryRun), shortcuts, log);

        if (options.Text is { } text)
        {
            Terminal.Out($"{apps.Names.Count} apps instalados, navegadores: {string.Join(", ", apps.Browsers)}, decisões: {(key is null ? "regras locais" : "Jev")}\n");
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await session.ReplayAsync(text, options.Wpm, options.Write);
                }
                finally
                {
                    log?.Dispose();
                    dispatcher.InvokeShutdown();
                }
            });
            Dispatcher.Run();
            return 0;
        }

        var model = new BarModel();
        session.Bar = model;
        var controller = new AppController(session, model, options, settings, log);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) => controller.Start();
        app.Run();
        return 0;
    }
}
