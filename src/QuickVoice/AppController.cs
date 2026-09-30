using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace QuickVoice;

/// <summary>The app: the floating bar, a hotkey to start or pause listening, and a tray icon with a menu.</summary>
internal sealed class AppController(Session session, BarModel model, Options options, Settings settings, EventLog? log)
{
    private BarWindow? bar;
    private Hotkey? hotkey;
    private Hotkey? writeHotkey;
    private Forms.NotifyIcon? tray;
    private Forms.ToolStripMenuItem? toggleItem;
    private Drawing.Icon? idleIcon, listeningIcon;
    private IListener? listener;
    private bool listening;
    private bool busy;
    private readonly History history = new();
    private readonly MicMeter meter = new();
    private Forms.ToolStripMenuItem? updateItem;
    private Updater.Release? update;

    public void Start()
    {
        bar = new BarWindow(model, settings);
        bar.Glow.Source = meter.Read;
        bar.Show();
        session.History = history;
        model.Toggle = Toggle;
        model.Submit = session.Submit;
        var handle = new WindowInteropHelper(bar).Handle;
        hotkey = new Hotkey(handle, 0x4A56, Hotkey.ListenFor(settings.Hotkey), Toggle);
        writeHotkey = new Hotkey(handle, 0x4A57, Hotkey.Write, bar.BeginWrite);
        model.Hotkey = hotkey.Label ?? "sem atalho";
        model.WriteHotkey = writeHotkey.Label ?? "botão ⌨";
        tray = MakeTray();
        ShowState();
        session.OnUtteranceEnd = () =>
        {
            if (listening) listener?.Restart();
        };
        if (hotkey.Label is null) model.Notice = "O atalho escolhido está em uso por outro app: use o botão ▶ ou troque em Configurações.";
        if (settings.WakeWord && settings.WakeList.Count > 0)
        {
            session.WakePhrases = settings.WakeList;
            Toggle();  // always listening, waiting for its name
        }
        if (settings.CheckUpdates) _ = CheckForUpdateAsync(quiet: true);
        if (options.Agent) bar.Dispatcher.InvokeAsync(OpenAgent, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (options.History) bar.Dispatcher.InvokeAsync(ShowHistory, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void ShowHistory() => new HistoryWindow(history, session.Submit).Show();

    /// <summary>Quiet at startup (only a newer version is mentioned); from the menu it also says "up to date".</summary>
    private async Task CheckForUpdateAsync(bool quiet)
    {
        update = await Updater.CheckAsync();
        if (update is null)
        {
            if (!quiet) model.Notice = $"O QuickVoice {Updater.Current.ToString(3)} está atualizado.";
            return;
        }
        if (updateItem is not null) updateItem.Text = $"Atualizar para a {update.Tag}…";
        model.Notice = $"Versão nova: {update.Tag}. Ícone na bandeja → Atualizar.";
    }

    private async void OnUpdate()
    {
        if (update is null)
        {
            await CheckForUpdateAsync(quiet: false);
            return;
        }
        var installed = Updater.Installed;
        var question = installed
            ? $"Baixar e instalar o QuickVoice {update.Tag}? Ele fecha e abre de novo sozinho."
            : $"Esta cópia não foi instalada pelo instalador. Abrir a página da {update.Tag} para baixar?";
        if (MessageBox.Show(question, "QuickVoice", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        model.Notice = installed ? $"Baixando a {update.Tag}…" : null;
        try
        {
            if (await Updater.InstallAsync(update)) Quit();
        }
        catch (Exception error) when (error is System.Net.Http.HttpRequestException or TaskCanceledException or IOException
                                          or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            model.Notice = $"A atualização falhou: {error.Message}";
        }
    }

    /// <summary>The user's shortcuts file, in Notepad.</summary>
    private void OpenShortcuts()
    {
        new Shortcuts().Reload();  // creates the commented sample the first time
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Shortcuts.FilePath}\""))?.Dispose();
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
                var locale = options.Locale ?? settings.Locale;
                IListener made = settings.Recognizer == "whisper"
                    ? await WhisperListener.CreateAsync(locale, settings.WhisperModel, bar!.Dispatcher, message => model.Notice = message)
                    : await Listener.CreateAsync(locale, bar!.Dispatcher);
                made.Partial += session.Heard;
                made.Ended += OnRecognizerEnded;
                session.StillSpeaking = () => made.Speaking;
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
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException
                                          or System.Net.Http.HttpRequestException or IOException or DllNotFoundException or NAudio.MmException)
        {
            model.Notice = settings.Recognizer == "whisper" ? $"O Whisper não iniciou: {error.Message}" : $"O microfone não iniciou: {error.Message}";
            return;
        }
        listening = true;
        model.Listening = true;
        if (settings.VoiceGlow != "off") meter.Start();
        model.Notice = session.WakePhrases.Count > 0 ? $"Sempre ouvindo: comece com “{session.WakePhrases[0]}”, ex. “{session.WakePhrases[0]}, abre o chrome”." : null;
        ShowState();
        log?.Write("listening", new() { ["language"] = listener.Language });
        Terminal.Out($"🎙 ouvindo em {listener.Language}\n");
    }

    private async Task StopAsync()
    {
        listening = false;  // before the session ends the utterance, so it does not restart the recognizer
        model.Listening = false;
        meter.Stop();
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
        meter.Dispose();
        (listener as IDisposable)?.Dispose();
        if (tray is not null)
        {
            tray.Visible = false;
            tray.Dispose();
        }
        log?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>Model, plugins and where the agent runs; read again by each task, so no restart.</summary>
    private void OpenAgent() => new AgentWindow(settings).ShowDialog();

    private void OpenSettings()
    {
        var dialog = new SettingsWindow(settings, PromptForKey, OpenAgent);
        if (dialog.ShowDialog() != true) return;
        try
        {
            settings.Save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            model.Notice = $"As configurações não foram salvas: {error.Message}";
            return;
        }
        var answer = MessageBox.Show("Reiniciar o QuickVoice agora para aplicar?", "QuickVoice", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--restarted") { UseShellExecute = false })?.Dispose();
        Quit();
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
        menu.Items.Add(new Forms.ToolStripMenuItem("Configurações…", null, (_, _) => OpenSettings()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Agente Copilot…", null, (_, _) => OpenAgent()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Editar meus atalhos…", null, (_, _) => OpenShortcuts()));
        menu.Items.Add(new Forms.ToolStripMenuItem("Histórico…", null, (_, _) => ShowHistory()));
        updateItem = new Forms.ToolStripMenuItem("Procurar atualizações", null, (_, _) => OnUpdate());
        menu.Items.Add(updateItem);
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
