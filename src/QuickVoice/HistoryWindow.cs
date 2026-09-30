using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickVoice;

/// <summary>The history as a paper shredder: drag a command into the slit to delete it, double-click to repeat it.</summary>
internal sealed class HistoryWindow : Window
{
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));
    private static readonly Brush Soft = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA));
    private static readonly Brush Faint = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A));

    public HistoryWindow(History history, Action<string> repeat)
    {
        Title = "QuickVoice · Histórico";
        Width = 560;
        Height = Math.Min(700, SystemParameters.WorkArea.Height - 40);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x13));
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var empty = new TextBlock
        {
            Text = "Nada por aqui ainda. O que você disser ao QuickVoice aparece nesta lista.", Foreground = Faint, FontSize = 13,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 40, 120),
        };
        ShredderList<History.Entry>? shredder = null;
        shredder = new ShredderList<History.Entry>
        {
            Items = () => history.Entries.Reverse().ToList(),  // oldest on top, the newest right above the slit
            RenderItem = Row,
            OnShred = entry =>
            {
                history.Remove(entry);
                empty.Visibility = history.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            },
            OnActivate = entry =>
            {
                Close();
                repeat(entry.Said);
            },
            Margin = new Thickness(18, 0, 18, 0),
        };
        empty.Visibility = history.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var header = new StackPanel { Margin = new Thickness(32, 22, 32, 14) };
        header.Children.Add(new TextBlock { Text = "Histórico", Foreground = Ink, FontSize = 22, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock
        {
            Text = "Arraste um comando até a fenda para apagar · clique duas vezes para repetir", Foreground = Soft, FontSize = 12.5,
            Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
        });

        var clear = FooterButton("Triturar tudo");
        clear.Click += async (_, _) =>
        {
            clear.IsEnabled = false;
            await shredder.ShredAllAsync();
            history.Clear();  // what did not fit on screen goes without the show
            shredder.Refresh();
            empty.Visibility = Visibility.Visible;
            clear.IsEnabled = true;
        };
        var close = FooterButton("Fechar");
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 0, 24, 18) };
        footer.Children.Add(clear);
        footer.Children.Add(close);

        var middle = new Grid();
        middle.Children.Add(shredder);
        middle.Children.Add(empty);
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(middle);
        Content = root;
    }

    private static FrameworkElement Row(History.Entry entry)
    {
        var done = entry.Done.Count > 0 ? "→ " + string.Join(" · ", entry.Done) : "nada feito";
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var said = new TextBlock { Text = $"“{entry.Said}”", Foreground = Ink, FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Bottom };
        var when = new TextBlock { Text = entry.At.ToString("dd/MM HH:mm"), Foreground = Faint, FontSize = 11, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
        var what = new TextBlock { Text = done, Foreground = Soft, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
        Grid.SetColumn(when, 1);
        Grid.SetRow(what, 1);
        Grid.SetColumnSpan(what, 2);
        grid.Children.Add(said);
        grid.Children.Add(when);
        grid.Children.Add(what);
        return new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 8, 14, 8), Child = grid,
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)), BorderThickness = new Thickness(1),
            ToolTip = $"{entry.Said}\n{done}",
        };
    }

    private static Button FooterButton(string text) => new()
    {
        Content = text, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(8, 0, 0, 0), Foreground = Ink, Cursor = System.Windows.Input.Cursors.Hand,
        Background = new SolidColorBrush(Color.FromRgb(0x27, 0x27, 0x2A)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
    };
}
