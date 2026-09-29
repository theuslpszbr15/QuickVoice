using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using QuickVoice.Core;

namespace QuickVoice;

/// <summary>
/// The pill (what it hears) and, while you talk, Jev's read below it: the three likeliest actions,
/// the yes/no "opens an app?", and each command as it fires. It never takes focus, so typed text
/// always lands in the app you are using; write mode borrows focus only while you type a command.
/// </summary>
internal partial class BarWindow : Window
{
    private const double Threshold = 0.8;
    private const double MeterWidth = 250;
    private static readonly Brush Fire = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF));
    private static readonly Brush IdleDisc = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));

    private readonly BarModel model;
    private readonly Storyboard wave = new() { RepeatBehavior = RepeatBehavior.Forever };
    private bool waving;
    private bool wasExpanded;
    private nint hwnd;
    private bool writing;
    private nint previousWindow;

    public BarWindow(BarModel model)
    {
        this.model = model;
        InitializeComponent();
        BuildWave();
        model.Changed += () => Dispatcher.InvokeAsync(Render);
        Deactivated += (_, _) =>
        {
            if (writing) EndWrite(restoreFocus: false);  // clicked away: the other app keeps the focus
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top;
            Render();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwnd = new WindowInteropHelper(this).Handle;
        SetNoActivate(true);
        var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, style | Native.WS_EX_TOOLWINDOW);
    }

    /// <summary>Opens the text field and takes the keyboard, remembering which app had it.</summary>
    public void BeginWrite()
    {
        if (!writing)
        {
            var front = Native.GetForegroundWindow();
            previousWindow = front == hwnd ? 0 : front;
            writing = true;
            SetNoActivate(false);
            Render();
        }
        Show();
        Activate();
        Native.SetForegroundWindow(hwnd);
        Input.Focus();
        Keyboard.Focus(Input);
    }

    private void EndWrite(bool restoreFocus)
    {
        writing = false;
        Input.Text = "";
        SetNoActivate(true);
        if (restoreFocus && previousWindow != 0) Native.SetForegroundWindow(previousWindow);
        Render();
    }

    private async void OnInputKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndWrite(restoreFocus: true);
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            var text = Input.Text;
            EndWrite(restoreFocus: true);
            await Task.Delay(150);  // let the previous app take the keyboard back before "digita…" types into it
            model.Submit(text);
        }
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e) =>
        InputHint.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnWrite(object sender, RoutedEventArgs e) => BeginWrite();

    private void SetNoActivate(bool on)
    {
        var style = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, on ? style | Native.WS_EX_NOACTIVATE : style & ~(nint)Native.WS_EX_NOACTIVATE);
    }

    private void OnToggle(object sender, RoutedEventArgs e) => model.Toggle();

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Render()
    {
        ToggleGlyph.Visibility = model.Listening ? Visibility.Collapsed : Visibility.Visible;
        ToggleButton.Background = model.Listening ? Brushes.Transparent : IdleDisc;
        ToggleButton.ToolTip = model.Listening ? $"Pausar ({model.Hotkey})" : $"Começar a ouvir ({model.Hotkey})";
        Wave.Visibility = model.Listening ? Visibility.Visible : Visibility.Collapsed;
        SetWaving(model.Listening);

        HotkeyText.Text = model.Hotkey;
        HotkeyChip.Visibility = model.Expanded || model.Listening || writing ? Visibility.Collapsed : Visibility.Visible;
        WriteButton.Visibility = model.Expanded || writing ? Visibility.Collapsed : Visibility.Visible;
        WriteButton.ToolTip = $"Escrever um comando ({model.WriteHotkey})";
        WriteBox.Visibility = writing ? Visibility.Visible : Visibility.Collapsed;
        Line.Visibility = writing ? Visibility.Collapsed : Visibility.Visible;
        var flashing = model.Expanded && model.Flash && model.FiredCommands.Count > 0;
        FireChip.Visibility = flashing ? Visibility.Visible : Visibility.Collapsed;
        if (flashing) FireText.Text = model.FiredCommands[^1].Command;
        StateText.Visibility = model.Expanded && !flashing ? Visibility.Visible : Visibility.Collapsed;
        StateText.Text = model.Paused ? "você pausou" : "ouvindo";

        RenderLine();
        RenderTray();
    }

    private void RenderLine()
    {
        Line.Inlines.Clear();
        Line.FontSize = 15;
        Line.TextWrapping = TextWrapping.NoWrap;
        Line.Foreground = Brushes.White;
        if (!model.Expanded && model.Notice is { } notice)
        {
            Line.FontSize = 13;
            Line.TextWrapping = TextWrapping.Wrap;
            Line.Inlines.Add(new Run(notice));
        }
        else if (!model.Expanded && model.FiredCommands.Count > 0)
        {
            var last = model.FiredCommands[^1];
            Line.FontSize = 14;
            Line.Inlines.Add(new Run(last.Command) { Foreground = Fire, FontWeight = FontWeights.SemiBold });
            if (last.Lead is { } lead) Line.Inlines.Add(new Run(" · " + lead) { Foreground = Muted });
        }
        else if (model.Words.Count == 0)
        {
            Line.Inlines.Add(new Run(model.Listening ? "Ouvindo…" : "Pausado") { Foreground = Muted });
        }
        else
        {
            foreach (var word in model.Words)
            {
                var run = new Run(word.Text + " ");
                if (word.Used)
                {
                    run.Foreground = Dim;
                    run.TextDecorations = TextDecorations.Strikethrough;
                }
                Line.Inlines.Add(run);
            }
            Dispatcher.InvokeAsync(TrimLineStart, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    /// <summary>The newest words matter: when the line is full, drop words from its start. Runs after layout, when the width is known.</summary>
    private void TrimLineStart()
    {
        var available = Line.ActualWidth;
        if (available <= 0) return;
        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        Line.Measure(unbounded);
        while (Line.Inlines.Count > 1 && Line.DesiredSize.Width > available)
        {
            Line.Inlines.Remove(Line.Inlines.FirstInline);
            Line.Measure(unbounded);
        }
        Line.InvalidateMeasure();
    }

    private void RenderTray()
    {
        var show = model.Expanded && model.Decision is not null;
        if (show && !wasExpanded)
        {
            Tray.Opacity = 0;
            Tray.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new QuadraticEase() });
        }
        wasExpanded = show;
        Tray.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Card.CornerRadius = Shadow.CornerRadius = new CornerRadius(show ? 20 : 22);
        if (!show || model.Decision is not { } decision) return;

        Meters.Children.Clear();
        foreach (var (action, probability) in decision.Top(3))
            Meters.Children.Add(Meter(action.Label(), probability, action == decision.Action ? Brushes.White : Dim));
        Meters.Children.Add(Meter("abre um app?", decision.OpensApp,
            decision.OpensApp >= Threshold ? Fire : new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xD6, 0x0A))));

        Tags.Children.Clear();
        if (decision.App is { } app) Tags.Children.Add(Chip($"app · {app}"));
        if (decision.Argument is { } argument && decision.Action is ActionKind.WebSearch or ActionKind.TypeText or ActionKind.OpenUrl)
            Tags.Children.Add(Chip($"texto · “{argument}”"));

        Fires.Children.Clear();
        foreach (var fired in model.FiredCommands) Fires.Children.Add(FireRow(fired));
    }

    private static Grid Meter(string label, double value, Brush fill)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MeterWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF)) });

        var track = new Grid { Height = 16, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(track, 1);
        track.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)) });
        track.Children.Add(new Border
        {
            Height = 8, CornerRadius = new CornerRadius(4), Background = fill, HorizontalAlignment = HorizontalAlignment.Left,
            Width = MeterWidth * Math.Clamp(value, 0, 1),
        });
        track.Children.Add(new Rectangle
        {
            Width = 1, Height = 16, Fill = new SolidColorBrush(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(MeterWidth * Threshold, 0, 0, 0),
        });
        row.Children.Add(track);

        var number = new TextBlock
        {
            Text = value.ToString("0.00"), FontSize = 12, FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(number, 2);
        row.Children.Add(number);
        return row;
    }

    private static Border Chip(string text) => new()
    {
        Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(13),
        Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
        Child = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 440 },
    };

    private static Border FireRow(BarModel.Fired fired)
    {
        var line = new TextBlock { FontSize = 13, Foreground = Fire, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        line.Inlines.Add(new Run("\uE945  ") { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12 });
        line.Inlines.Add(new Run(fired.Command) { FontWeight = FontWeights.SemiBold });
        if (fired.Lead is { } lead) line.Inlines.Add(new Run("  " + lead) { Foreground = Muted });
        return new Border
        {
            MinHeight = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 4, 0, 0), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xD6, 0x0A)), Child = line,
        };
    }

    /// <summary>Decorative, like the original: not the real mic level.</summary>
    private void BuildWave()
    {
        var index = 0;
        foreach (var bar in Wave.Children.OfType<Rectangle>())
        {
            var scale = new ScaleTransform(1, 0.35);
            bar.RenderTransform = scale;
            var animation = new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(320 + 60 * index))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(110 * index),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(animation, bar);
            Storyboard.SetTargetProperty(animation, new PropertyPath("RenderTransform.ScaleY"));
            wave.Children.Add(animation);
            index++;
        }
    }

    private void SetWaving(bool on)
    {
        if (on == waving) return;
        waving = on;
        if (on) wave.Begin(this, true);
        else wave.Stop(this);
    }
}
