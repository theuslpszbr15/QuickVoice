using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace QuickVoice;

/// <summary>The app: the floating bar, a hotkey to start or pause listening, and a tray icon with a menu.</summary>
internal sealed class AppController(Session session, BarModel model, Options options, EventLog? log)
{
    private BarWindow? bar;
    private Hotkey? hotkey;
    private Hotkey? writeHotkey;
    private Forms.NotifyIcon? tray;
    private Forms.ToolStripMenuItem? toggleItem;
    private Drawing.Icon? idleIcon, listeningIcon;
    private Listener? listener;
    private bool listening;
    private bool busy;

    public void Start()
    {
        bar = new BarWindow(model);
        bar.Show();
        model.Toggle = Toggle;
        model.Submit = session.Submit;
        var handle = new WindowInteropHelper(bar).Handle;
        hotkey = new Hotkey(handle, 0x4A56, Hotkey.Listen, Toggle);
        writeHotkey = new Hotkey(handle, 0x4A57, Hotkey.Write, bar.BeginWrite);
        model.Hotkey = hotkey.Label ?? "sem atalho";
        model.WriteHotkey = writeHotkey.Label ?? "botão ⌨";
        tray = MakeTray();
        ShowState();
        session.OnUtteranceEnd = () =>
        {
            if (listening) listener?.Restart();
        };
        if (hotkey.Label is null) model.Notice = "Alt+Espaço e Ctrl+Alt+Espaço estão em uso por outro app: use o botão ▶.";
    }

    /// <summary>Optional: with a Jev key the decisions come from Jev instead of the local rules.</summary>
    private void PromptForKey()
    {
        var dialog = new KeyWindow();
        if (dialog.ShowDialog() != true) return;
        try
        {
            ApiKey.Save(dialog.Key);
            session.Use(dialog.Key);
            model.Notice = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            model.Notice = $"A chave não foi salva: {error.Message}";
        }
    }

    private async void Toggle()
    {
        if (busy) return;  // starting the recognizer takes a moment; a second press waits for it
        busy = true;
        try
        {
            if (listening) await StopAsync();
            else await StartAsync();
        }
        finally
        {
            busy = false;
        }
    }

    private async Task StartAsync()
    {
        try
        {
            if (listener is null)
            {
                var made = await Listener.CreateAsync(options.Locale, bar!.Dispatcher);
                made.Partial += session.Heard;
                made.Ended += OnRecognizerEnded;
                listener = made;
            }
            await listener.StartAsync();
        }
        catch (ListenerException error)
        {
            model.Notice = error.Message;
            if (error.OpenSettings is { } page) Process.Start(new ProcessStartInfo(page) { UseShellExecute = true })?.Dispose();
            return;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            model.Notice = $"O microfone não iniciou: {error.Message}";
            return;
        }
        listening = true;
        model.Listening = true;
        model.Notice = null;
        ShowState();
        log?.Write("listening", new() { ["language"] = listener.Language });
        Terminal.Out($"🎙 ouvindo em {listener.Language}\n");
    }

    private async Task StopAsync()
    {
        listening = false;  // before the session ends the utterance, so it does not restart the recognizer
        model.Listening = false;
        if (listener is not null) await listener.StopAsync();
        session.Stop();
        log?.Write("stopped");
        ShowState();
    }

    /// <summary>Windows stopped the recognizer (mic unplugged, network lost for online dictation): try once to resume.</summary>
    private async void OnRecognizerEnded(string status)
    {
        log?.Write("recognizer_ended", new() { ["status"] = status });
        session.RecognizerEnded();
        if (!listening || listener is null) return;
        try
        {
            await listener.StartAsync();
        }
        catch (Exception error)
        {
            listening = false;
            model.Listening = false;
            model.Notice = $"O reconhecimento de fala parou ({status}): {error.Message}";
            ShowState();
        }
    }

    private void Quit()
    {
        hotkey?.Dispose();
        writeHotkey?.Dispose();
        if (tray is not null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        log?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>The tray icon and its menu say whether QuickVoice is listening.</summary>
    private void ShowState()
    {
        if (tray is null || toggleItem is null) return;
        tray.Icon = listening ? listeningIcon : idleIcon;
        tray.Text = listening ? "QuickVoice está ouvindo" : "QuickVoice está pausado";
        toggleItem.Text = listening ? $"Pausar ({model.Hotkey})" : $"Começar a ouvir ({model.Hotkey})";
    }

    private Forms.NotifyIcon MakeTray()
    {
        idleIcon = TrayIcon(listening: false);
        listeningIcon = TrayIcon(listening: true);
        var menu = new Forms.ContextMenuStrip();
        toggleItem = new Forms.ToolStripMenuItem("Começar a ouvir", null, (_, _) => Toggle());
        menu.Items.Add(toggleItem);
        menu.Items.Add(new Forms.ToolStripMenuItem("Usar chave do Jev (opcional, pago)…", null, (_, _) => PromptForKey()));
        menu.Items.Add(new Forms.ToolStripMenuItem($"Escrever um comando ({model.WriteHotkey})", null, (_, _) => bar?.BeginWrite()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Mostrar a barra", null, (_, _) => bar?.Show()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Sair do QuickVoice", null, (_, _) => Quit()));
        var icon = new Forms.NotifyIcon { ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) Toggle();
        };
        return icon;
    }

    /// <summary>The speech bubble, half said and half dashed; a green dot while listening.</summary>
    private static Drawing.Icon TrayIcon(bool listening)
    {
        var size = Forms.SystemInformation.SmallIconSize;
        using var stream = typeof(AppController).Assembly.GetManifestResourceStream("app.ico")!;
        using var bubble = new Drawing.Icon(stream, size);
        if (!listening) return (Drawing.Icon)bubble.Clone();
        using var bitmap = bubble.ToBitmap();
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var d = size.Width * 0.45f;
            using var ring = new Drawing.SolidBrush(Drawing.Color.FromArgb(29, 29, 31));
            using var dot = new Drawing.SolidBrush(Drawing.Color.FromArgb(48, 209, 88));
            g.FillEllipse(ring, size.Width - d - 0.5f, size.Height - d - 0.5f, d + 0.5f, d + 0.5f);
            g.FillEllipse(dot, size.Width - d + 0.75f, size.Height - d + 0.75f, d - 2f, d - 2f);
        }
        return Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}
