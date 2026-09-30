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
    // Theme: set by ApplyTheme before the bar renders (dark and yellow until then).
    private Color ink = Colors.White;
    private Color accent = Color.FromRgb(0xFF, 0xD6, 0x0A);
    private Brush Fire = new SolidColorBrush(Color.FromRgb(0xFF, 0xD6, 0x0A));
    private Brush Dim => Ink(0x59);
    private Brush Muted => Ink(0xA6);
    private Brush IdleDisc => Ink(0x24);

    private readonly BarModel model;
    private readonly Storyboard wave = new() { RepeatBehavior = RepeatBehavior.Forever };
    private bool waving;
    private bool wasExpanded;
    private nint hwnd;
    private bool writing;
    private nint previousWindow;

    public BarWindow(BarModel model, Settings settings)
    {
        this.model = model;
        InitializeComponent();
        ApplyTheme(settings);
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

    private SolidColorBrush Ink(byte alpha) => new(Color.FromArgb(alpha, ink.R, ink.G, ink.B));

    /// <summary>Light or dark, the accent of what fires, and the size, from the settings.</summary>
    private void ApplyTheme(Settings settings)
    {
        var light = settings.Theme == "light";
        ink = light ? Color.FromRgb(0x1D, 0x1D, 0x1F) : Colors.White;
        // Bright for the chip; on the light bar, text in the accent needs a darker shade to be read.
        var (bright, dark, chipInk) = settings.Accent switch
        {
            "blue" => (Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x00, 0x5F, 0xCC), Colors.White),
            "green" => (Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0x1E, 0x8E, 0x3E), Colors.Black),
            "pink" => (Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0xD0, 0x1F, 0x4A), Colors.White),
            _ => (Color.FromRgb(0xFF, 0xD6, 0x0A), Color.FromRgb(0x9A, 0x6B, 0x00), Colors.Black),
        };
        accent = light ? dark : bright;
        Fire = new SolidColorBrush(accent);
        Resources["Fire"] = new SolidColorBrush(bright);
        Resources["FireInk"] = new SolidColorBrush(chipInk);
        Resources["Panel"] = new SolidColorBrush(light ? Color.FromArgb(0xF7, 0xFB, 0xFB, 0xFC) : Color.FromArgb(0xF5, 0x42, 0x42, 0x46));
        Resources["Ink"] = Ink(0xFF);
        foreach (var (key, alpha) in new[] { ("Ink08", 0x14), ("Ink14", 0x24), ("Ink24", 0x3D), ("Ink33", 0x55), ("Ink45", 0x73), ("Ink50", 0x80), ("Ink55", 0x8C), ("Ink70", 0xB3) })
            Resources[key] = Ink((byte)alpha);
        Foreground = Ink(0xFF);
        var scale = settings.BarSize switch { "small" => 0.85, "large" => 1.2, _ => 1.0 };
        Root.LayoutTransform = new ScaleTransform(scale, scale);
        Width = 568 * scale;
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
        Line.Foreground = Ink(0xFF);
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
            Meters.Children.Add(Meter(action.Label(), probability, action == decision.Action ? Ink(0xFF) : Dim));
        Meters.Children.Add(Meter("abre um app?", decision.OpensApp,
            decision.OpensApp >= Threshold ? Fire : new SolidColorBrush(Color.FromArgb(0x73, accent.R, accent.G, accent.B))));

        Tags.Children.Clear();
        if (decision.App is { } app) Tags.Children.Add(Chip($"app · {app}"));
        if (decision.Argument is { } argument && decision.Action is ActionKind.WebSearch or ActionKind.TypeText or ActionKind.OpenUrl)
            Tags.Children.Add(Chip($"texto · “{argument}”"));

        Fires.Children.Clear();
        foreach (var fired in model.FiredCommands) Fires.Children.Add(FireRow(fired));
    }

    private Grid Meter(string label, double value, Brush fill)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MeterWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Ink(0xD9) });

        var track = new Grid { Height = 16, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(track, 1);
        track.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Ink(0x1A) });
        track.Children.Add(new Border
        {
            Height = 8, CornerRadius = new CornerRadius(4), Background = fill, HorizontalAlignment = HorizontalAlignment.Left,
            Width = MeterWidth * Math.Clamp(value, 0, 1),
        });
        track.Children.Add(new Rectangle
        {
            Width = 1, Height = 16, Fill = Ink(0x8C),
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

    private Border Chip(string text) => new()
    {
        Height = 26, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 0, 6, 6), CornerRadius = new CornerRadius(13),
        Background = Ink(0x1A),
        Child = new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 440 },
    };

    private Border FireRow(BarModel.Fired fired)
    {
        var line = new TextBlock { FontSize = 13, Foreground = Fire, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        line.Inlines.Add(new Run("\uE945  ") { FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12 });
        line.Inlines.Add(new Run(fired.Command) { FontWeight = FontWeights.SemiBold });
        if (fired.Lead is { } lead) line.Inlines.Add(new Run("  " + lead) { Foreground = Muted });
        return new Border
        {
            MinHeight = 32, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 4, 0, 0), CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(0x24, accent.R, accent.G, accent.B)), Child = line,
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
