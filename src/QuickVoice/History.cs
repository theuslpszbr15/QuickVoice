using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace QuickVoice;

/// <summary>What was said and done, newest first, in %LOCALAPPDATA%\QuickVoice\historico.json (never leaves the PC).</summary>
internal sealed class History
{
    public sealed record Entry(DateTime At, string Said, IReadOnlyList<string> Done);

    private const int Keep = 200;
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickVoice", "historico.json");

    private List<Entry> entries = Load();

    public IReadOnlyList<Entry> Entries => entries;

    public void Add(string said, IEnumerable<string> done)
    {
        entries.Insert(0, new Entry(DateTime.Now, said, done.ToList()));
        if (entries.Count > Keep) entries = entries[..Keep];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // the history is a convenience: a full or locked disk must not stop commands
        }
    }

    public void Clear()
    {
        entries = [];
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static List<Entry> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}

/// <summary>The history list: pick a line and repeat it.</summary>
internal sealed class HistoryWindow : Window
{
    public HistoryWindow(History history, Action<string> repeat)
    {
        Title = "QuickVoice · Histórico";
        Width = 560;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var list = new ListBox { Margin = new Thickness(12, 12, 12, 6) };
        void Fill()
        {
            list.Items.Clear();
            foreach (var entry in history.Entries)
            {
                var done = entry.Done.Count > 0 ? string.Join(" · ", entry.Done) : "nada feito";
                list.Items.Add(new ListBoxItem
                {
                    Tag = entry.Said,
                    Content = new TextBlock { Text = $"{entry.At:dd/MM HH:mm}  “{entry.Said}”\n      → {done}", TextWrapping = TextWrapping.Wrap },
                });
            }
        }
        Fill();
        void RepeatSelected()
        {
            if (list.SelectedItem is not ListBoxItem { Tag: string said }) return;
            Close();
            repeat(said);
        }
        list.MouseDoubleClick += (_, _) => RepeatSelected();
        var again = new Button { Content = "Repetir", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        again.Click += (_, _) => RepeatSelected();
        var clear = new Button { Content = "Limpar histórico", Padding = new Thickness(16, 4, 16, 4) };
        clear.Click += (_, _) =>
        {
            history.Clear();
            Fill();
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
        buttons.Children.Add(again);
        buttons.Children.Add(clear);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(list);
        Content = root;
    }
}
