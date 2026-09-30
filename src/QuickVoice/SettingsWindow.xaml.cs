using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace QuickVoice;

internal partial class SettingsWindow : Window
{
    private readonly Settings settings;
    private readonly Action promptForKey;

    public SettingsWindow(Settings settings, Action promptForKey)
    {
        this.settings = settings;
        this.promptForKey = promptForKey;
        InitializeComponent();
        Select(LocaleBox, settings.Locale ?? "");
        Select(ModelBox, settings.WhisperModel);
        Select(HotkeyBox, settings.Hotkey);
        WindowsRadio.IsChecked = settings.Recognizer != "whisper";
        WhisperRadio.IsChecked = settings.Recognizer == "whisper";
        WakeCheck.IsChecked = settings.WakeWord;
        WakeBox.Text = settings.WakePhrases;
        Select(ThemeBox, settings.Theme);
        Select(AccentBox, settings.Accent);
        Select(SizeBox, settings.BarSize);
        UpdatesCheck.IsChecked = settings.CheckUpdates;
        Loaded += (_, _) => Activate();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var locale = TagOf(LocaleBox);
        settings.Locale = string.IsNullOrEmpty(locale) ? null : locale;
        settings.Recognizer = WhisperRadio.IsChecked == true ? "whisper" : "windows";
        settings.WhisperModel = TagOf(ModelBox) ?? "base";
        settings.Hotkey = TagOf(HotkeyBox) ?? "auto";
        settings.WakeWord = WakeCheck.IsChecked == true;
        settings.WakePhrases = string.IsNullOrWhiteSpace(WakeBox.Text) ? "QuickVoice" : WakeBox.Text.Trim();
        settings.Theme = TagOf(ThemeBox) ?? "dark";
        settings.Accent = TagOf(AccentBox) ?? "yellow";
        settings.BarSize = TagOf(SizeBox) ?? "normal";
        settings.CheckUpdates = UpdatesCheck.IsChecked == true;
        DialogResult = true;
    }

    private void OnShortcuts(object sender, RoutedEventArgs e)
    {
        new Shortcuts().Reload();  // creates the commented sample the first time
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Shortcuts.FilePath}\""))?.Dispose();
    }

    private void OnKey(object sender, RoutedEventArgs e) => promptForKey();

    private void OnLogs(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(EventLog.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{EventLog.Folder}\""))?.Dispose();
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];

    private static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;
}
