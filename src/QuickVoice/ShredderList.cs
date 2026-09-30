using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace QuickVoice;

/// <summary>
/// A list with a paper shredder at the bottom: drag a row into the slit and the rollers pull it through and cut it
/// into strips that curl, tumble and fade; the rows above settle on a spring. Adapted for WPF from React Bits'
/// Shredder (https://reactbits.dev/micro/shredder), keeping its constants.
/// </summary>
internal sealed class ShredderList<T> : Canvas where T : class
{
    // React Bits constants.
    private const double Slit = 6, Over = 40, StackK = 320, StackC = 22, Stagger = 0.035, Enter = 22, Tug = 2.4, TugDecay = 0.14;
    private const double Follow = 30, Settle = 16, DragTilt = 6, Lift = 1.02;

    public double Inset { get; init; } = 14;
    public double Gap { get; init; } = 10;
    public double RowHeight { get; init; } = 58;
    public double SlitHeight { get; init; } = 4;
    public double FallHeight { get; init; } = 140;
    public double FeedSpeed { get; init; } = 180;
    public double Bite { get; init; } = 18;
    public double StripWidth { get; init; } = 10;
    public double Curl { get; init; } = 1;

    /// <summary>Every item, top of the list first; the rows that fit are the last ones, nearest the slit.</summary>
    public required Func<IReadOnlyList<T>> Items { get; init; }
    public required Func<T, FrameworkElement> RenderItem { get; init; }
    /// <summary>A row went through the rollers: delete it here.</summary>
    public Action<T>? OnShred { get; init; }
    /// <summary>A row was double-clicked.</summary>
    public Action<T>? OnActivate { get; init; }

    private sealed class Row(T item, FrameworkElement element)
    {
        public T Item { get; } = item;
        public FrameworkElement Element { get; } = element;
        public double Home, Y, V, Delay, Opacity = 1;
    }

    private sealed class Drag(Row row)
    {
        public Row Row { get; } = row;
        public double Gx, Gy, Tx, Ty, Rx, Ry, Vx, Vy, Tilt, Lift = 1;
        public bool Returning, Moved;
    }

    private sealed class Feed(Row row)
    {
        public Row Row { get; } = row;
        public double Rx, Ry, V, Age, Tilt, Lift;
        public BitmapSource? Texture;
        public List<Strip> Strips { get; } = [];
    }

    private sealed class Strip
    {
        public double X, W, CurlRate, Amp, Wave, RA, RB, Ax, Ay, Vx, Vy, Th, Rest, Speed, Splay, Core = 1, Alpha = 1, Len, Hang;
        public bool Free;
        public Rectangle Shape = null!;
        public ImageBrush Brush = null!;
        public double RowLeft, RowH;
    }

    private readonly Canvas rowsLayer = new() { ClipToBounds = true };
    private readonly Canvas fallLayer = new() { IsHitTestVisible = false };
    private readonly Border slit = new() { CornerRadius = new CornerRadius(999) };
    private readonly List<Row> rows = [];
    private readonly List<Feed> feeds = [];
    private readonly Random random = new();
    private Drag? drag;
    private bool running;
    private TimeSpan last;
    private double time;

    private double Lip => ActualHeight - FallHeight - SlitHeight;
    private double RowWidth => ActualWidth - 2 * Inset;

    public ShredderList()
    {
        ClipToBounds = true;
        Background = Brushes.Transparent;
        Children.Add(rowsLayer);
        Children.Add(fallLayer);
        Children.Add(slit);
        slit.Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46));
        SizeChanged += (_, _) => Layout(animate: false);
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) => Release();
    }

    public Brush SlitColor { set => slit.Background = value; }

    /// <summary>Rebuilds the rows from <see cref="Items"/> (after a delete, or to show the next older ones).</summary>
    public void Refresh() => Layout(animate: true);

    /// <summary>Feeds the visible rows, bottom first, each after the last one is through. Returns how many went.</summary>
    public async Task<int> ShredAllAsync()
    {
        var count = 0;
        for (var left = rows.Count; left > 0; left--)
        {
            if (rows.LastOrDefault(r => feeds.All(f => f.Row != r) && drag?.Row != r) is not { } row) break;
            Grab(row, Canvas.GetLeft(row.Element), row.Home + row.Y, 0, 1);
            count++;
            while (rows.Contains(row)) await Task.Delay(30);
            await Task.Delay(90);
        }
        return count;
    }

    private void Layout(bool animate, int from = -1)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        rowsLayer.Width = ActualWidth;
        rowsLayer.Height = Math.Max(0, Lip);
        Canvas.SetTop(fallLayer, Lip + SlitHeight);
        fallLayer.Width = ActualWidth;
        fallLayer.Height = FallHeight;
        slit.Width = Math.Max(0, ActualWidth - 2 * Math.Max(0, Inset - Slit));
        slit.Height = SlitHeight;
        Canvas.SetLeft(slit, Math.Max(0, Inset - Slit));
        Canvas.SetTop(slit, Lip);

        var fits = Math.Max(1, (int)((Lip - 8) / (RowHeight + Gap)));
        var all = Items();
        var wanted = all.Skip(Math.Max(0, all.Count - fits)).ToList();
        var busy = feeds.Select(f => f.Row).Append(drag?.Row).Where(r => r is not null).ToHashSet();

        var previous = new Dictionary<T, Row>(ReferenceEqualityComparer.Instance);
        foreach (var r in rows) previous[r.Item] = r;
        foreach (var row in rows.Where(r => !wanted.Contains(r.Item) && !busy.Contains(r)).ToList())
        {
            rows.Remove(row);
            rowsLayer.Children.Remove(row.Element);
        }
        var next = new List<Row>();
        var born = 0;
        foreach (var item in wanted)
        {
            if (previous.TryGetValue(item, out var row) && rows.Contains(row))
            {
                next.Add(row);
                continue;
            }
            var element = RenderItem(item);
            element.Width = RowWidth;
            element.Height = RowHeight;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.Cursor = Cursors.Hand;
            row = new Row(item, element);
            element.MouseLeftButtonDown += (_, e) => OnDown(row, e);
            rowsLayer.Children.Add(element);
            if (animate)
            {
                row.Y = -Enter;  // a newcomer drops in from above
                row.Opacity = 0;
                row.Delay = born++ * 0.05;
            }
            next.Add(row);
        }
        // Rows that moved keep their old spot and spring to the new one; those above the gap go a beat later.
        rows.Clear();
        rows.AddRange(next);
        var top = Lip - Gap;
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            var row = rows[i];
            var home = top - RowHeight;
            top = home - Gap;
            if (row.Home != 0 && animate && Math.Abs(row.Home - home) >= 0.5)
            {
                row.Y += row.Home - home;
                if (from >= 0) row.Delay = Math.Max(0, from - 1 - i) * Stagger;
            }
            row.Home = home;
            row.Element.Width = RowWidth;
            Canvas.SetLeft(row.Element, Inset);
            Canvas.SetTop(row.Element, home + row.Y);
            row.Element.Opacity = row.Opacity;
        }
        Run();
    }

    private void OnDown(Row row, MouseButtonEventArgs e)
    {
        if (feeds.Any(f => f.Row == row) || drag is not null) return;
        if (e.ClickCount == 2)
        {
            OnActivate?.Invoke(row.Item);
            return;
        }
        var p = e.GetPosition(this);
        var x = Canvas.GetLeft(row.Element);
        var y = row.Home + row.Y;
        drag = new Drag(row) { Gx = p.X - x, Gy = p.Y - y, Tx = x, Ty = y, Rx = x, Ry = y };
        row.Element.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 14, Direction = 270, Opacity = 0.22, Color = Colors.Black };
        Panel.SetZIndex(row.Element, 10);
        row.Element.Cursor = Cursors.SizeAll;
        CaptureMouse();
        e.Handled = true;
        Run();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (drag is not { Returning: false } d) return;
        var p = e.GetPosition(this);
        d.Tx = p.X - d.Gx;
        d.Ty = Math.Min(p.Y - d.Gy, Lip + Bite - RowHeight);  // the bite line is as deep as a hand can push
        d.Moved = true;
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (drag is { Returning: false } d && d.Ry + RowHeight > Lip + 0.5)
        {
            drag = null;
            ReleaseMouseCapture();
            Grab(d.Row, d.Rx, d.Ry, d.Tilt, d.Lift);
            return;
        }
        Release();
    }

    private void Release()
    {
        if (drag is { Returning: false } d)
        {
            d.Returning = true;
            d.Tx = Inset;
            d.Ty = d.Row.Home + d.Row.Y;
        }
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    /// <summary>The rollers take the row: it is pulled down through the slit and cut.</summary>
    private void Grab(Row row, double x, double y, double tilt, double lift)
    {
        if (drag?.Row == row) drag = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        row.Element.Effect = null;
        var feed = new Feed(row) { Rx = x, Ry = y, V = FeedSpeed * Tug, Tilt = tilt, Lift = lift, Texture = Snapshot(row.Element) };
        var n = Math.Max(1, (int)Math.Round(RowWidth / Math.Max(4, StripWidth)));
        var sw = Math.Floor(RowWidth / n);
        for (var i = 0; i < n; i++)
        {
            var strip = new Strip
            {
                X = i * sw, W = i == n - 1 ? RowWidth - sw * (n - 1) : sw,
                CurlRate = Side() * Pick(0.35, 0.9) * Math.PI / 180 * Curl, Amp = Pick(1.5, 4) * Math.PI / 180 * Curl,
                Wave = Pick(0, Math.PI * 2), RA = random.NextDouble(), RB = random.NextDouble(), RowH = RowHeight,
            };
            strip.Ax = strip.X + strip.W / 2;
            strip.Brush = new ImageBrush(feed.Texture) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            strip.Shape = new Rectangle { Fill = strip.Brush, RenderTransformOrigin = new Point(0.5, 0), Visibility = Visibility.Collapsed };
            fallLayer.Children.Add(strip.Shape);
            feed.Strips.Add(strip);
        }
        feeds.Add(feed);
        Run();
    }

    private static BitmapSource Snapshot(FrameworkElement element)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var w = Math.Max(1, element.ActualWidth);
        var h = Math.Max(1, element.ActualHeight);
        var picture = new DrawingVisual();
        using (var dc = picture.RenderOpen())
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, w, h) }, null, new Rect(0, 0, w, h));
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(picture);
        bitmap.Freeze();
        return bitmap;
    }

    private void Run()
    {
        if (running) return;
        running = true;
        last = TimeSpan.Zero;
        CompositionTarget.Rendering += Tick;
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = last == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - last).TotalSeconds, 0, 0.05);
        last = now;
        if (dt <= 0) return;
        time += dt;
        var moving = false;

        if (drag is { } d)
        {
            var omega = d.Returning ? Settle : Follow;
            (d.Rx, d.Vx) = Spring(d.Rx, d.Vx, d.Tx, dt, omega);
            (d.Ry, d.Vy) = Spring(d.Ry, d.Vy, d.Ty, dt, omega);
            var lean = d.Returning ? 0 : Math.Clamp(d.Vx * 0.012, -DragTilt, DragTilt);
            d.Tilt = Ease(d.Tilt, lean, dt, 0.09);
            d.Lift = Ease(d.Lift, d.Returning ? 1 : Lift, dt, 0.12);
            Place(d.Row.Element, d.Rx, d.Ry, d.Tilt, d.Lift);
            if (!d.Returning && d.Ry + RowHeight - Lip >= Bite - 0.5)
                Grab(d.Row, d.Rx, d.Ry, d.Tilt, d.Lift);  // pushed deep enough: the rollers bite
            else if (d.Returning && Math.Abs(d.Rx - d.Tx) < 0.3 && Math.Abs(d.Ry - d.Ty) < 0.3 && Math.Abs(d.Tilt) < 0.05 && d.Lift < 1.001)
            {
                d.Row.Element.Effect = null;
                d.Row.Element.Cursor = Cursors.Hand;
                Panel.SetZIndex(d.Row.Element, 0);
                Place(d.Row.Element, Inset, d.Row.Home + d.Row.Y, 0, 1);
                drag = null;
            }
            moving = true;
        }

        for (var i = feeds.Count - 1; i >= 0; i--)
        {
            var f = feeds[i];
            f.Age += dt;
            f.V = FeedSpeed * (1 + (Tug - 1) * Math.Exp(-f.Age / TugDecay));  // a hard first tug that eases into the roller speed
            f.Ry += f.V * dt;
            f.Rx = Ease(f.Rx, Inset, dt, 0.1);
            f.Tilt = Ease(f.Tilt, 0, dt, 0.08);
            f.Lift = Ease(f.Lift, 1, dt, 0.1);
            var through = f.Ry + RowHeight - Lip;  // how much has come out under the slit
            if (f.Ry >= Lip && f.Row.Element.Visibility == Visibility.Visible)
            {
                f.Row.Element.Visibility = Visibility.Hidden;
                var from = rows.IndexOf(f.Row);
                rows.Remove(f.Row);
                rowsLayer.Children.Remove(f.Row.Element);
                OnShred?.Invoke(f.Row.Item);
                Layout(animate: true, from);
            }
            else if (f.Row.Element.Visibility == Visibility.Visible)
            {
                Place(f.Row.Element, f.Rx, f.Ry, f.Tilt, f.Lift);
            }
            var alive = false;
            foreach (var st in f.Strips)
            {
                if (st.Alpha <= 0.01) continue;
                st.RowLeft = f.Rx;
                if (!st.Free)
                {
                    st.Hang = Math.Min(RowHeight, Math.Max(0, through));
                    if (f.Ry >= Lip)
                    {
                        st.Free = true;  // cut loose: falls at its own pace, splays and tumbles
                        st.Ay = f.Ry - Lip;
                        st.Vy = f.V;
                        st.Vx = Pick(-20, 20);
                        st.Speed = 150 + st.RA * 230;
                        st.Splay = (st.RA - 0.5) * 320;
                        st.Rest = Side() * Pick(0.15, 0.6);
                    }
                    st.Len = st.Hang;
                }
                if (st.Free)
                {
                    var tear = Math.Pow(Math.Clamp((st.Ay + st.Len / 2) / FallHeight, 0, 1), 1.2);
                    var breath = 0.85 + 0.15 * Math.Sin(time * (1 + st.RB * 2) + st.RA * 6.2832);
                    st.Vy = Ease(st.Vy, st.Speed * breath, dt, 0.25);
                    st.Vx = Ease(st.Vx, st.Splay * tear + (st.Ax - RowWidth / 2) * 0.25, dt, 0.4);
                    st.Ax += st.Vx * dt;
                    st.Ay += st.Vy * dt;
                    st.Th = Ease(st.Th, st.Rest, dt, 0.5);
                    st.Core = 1 - Smooth(tear / 0.85) * (0.8 - st.RA * 0.16);
                    st.Alpha = 1 - Smooth((tear - 0.6) / 0.4);
                    if (st.Ay > FallHeight) st.Alpha = 0;
                }
                DrawStrip(st);
                alive |= st.Alpha > 0.01;
            }
            if (!alive && f.Row.Element.Visibility != Visibility.Visible)
            {
                foreach (var st in f.Strips) fallLayer.Children.Remove(st.Shape);
                feeds.RemoveAt(i);
            }
            moving = true;
        }
        slit.RenderTransform = feeds.Any(f => f.Row.Element.Visibility == Visibility.Visible)
            ? new TranslateTransform(Math.Sin(time * 140) * 0.5, Math.Cos(time * 97) * 0.35)  // the machine hums while it pulls
            : Transform.Identity;

        foreach (var row in rows)
        {
            if (drag?.Row == row || feeds.Any(f => f.Row == row)) continue;
            if (row.Delay > 0)
            {
                row.Delay -= dt;
                moving = true;
                continue;
            }
            if (Math.Abs(row.Y) > 0.05 || Math.Abs(row.V) > 0.05 || row.Opacity < 1)
            {
                var n = (int)Math.Ceiling(dt * 240);
                var h = dt / n;
                for (var k = 0; k < n; k++)
                {
                    row.V += (-StackK * row.Y - StackC * row.V) * h;
                    row.Y += row.V * h;
                }
                row.Opacity = Math.Min(1, row.Opacity + dt / 0.25);
                row.Element.Opacity = row.Opacity;
                Canvas.SetTop(row.Element, row.Home + row.Y);
                moving = true;
            }
            else if (row.Y != 0)
            {
                row.Y = row.V = 0;
                Canvas.SetTop(row.Element, row.Home);
            }
        }

        if (moving) return;
        running = false;
        CompositionTarget.Rendering -= Tick;
    }

    /// <summary>A strip hangs from its top: angle from its rest tilt, its curl along the length and a slow flutter.</summary>
    private void DrawStrip(Strip st)
    {
        if (st.Len < 0.5)
        {
            st.Shape.Visibility = Visibility.Collapsed;
            return;
        }
        var wc = st.W * st.Core;
        st.Brush.Viewbox = new Rect(st.X + (st.W - wc) / 2, st.RowH - st.Len, Math.Max(0.5, wc), st.Len);
        st.Shape.Width = Math.Max(0.5, wc);
        st.Shape.Height = st.Len;
        st.Shape.Opacity = Math.Clamp(st.Alpha, 0, 1);
        var angle = st.Th + st.CurlRate * st.Len * 0.5 + st.Amp * Math.Sin(st.Len * 0.5 * (Math.PI * 2 / 45) + st.Wave + time * 2);
        st.Shape.RenderTransform = new RotateTransform(-angle * 180 / Math.PI);
        Canvas.SetLeft(st.Shape, st.RowLeft + st.Ax - wc / 2);
        Canvas.SetTop(st.Shape, st.Ay);
        st.Shape.Visibility = Visibility.Visible;
    }

    private static void Place(FrameworkElement element, double x, double y, double tilt, double lift)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        element.RenderTransform = new TransformGroup { Children = { new ScaleTransform(lift, lift), new RotateTransform(tilt) } };
    }

    /// <summary>A critically damped spring, exact for a frame of any length.</summary>
    private static (double P, double V) Spring(double p, double v, double target, double dt, double omega)
    {
        var offset = p - target;
        var term = v + omega * offset;
        var decay = Math.Exp(-omega * dt);
        return (target + (offset + term * dt) * decay, (v - omega * term * dt) * decay);
    }

    private static double Ease(double current, double target, double dt, double tau) => current + (target - current) * (1 - Math.Exp(-dt / tau));
    private static double Smooth(double x) => Math.Clamp(x, 0, 1) * Math.Clamp(x, 0, 1) * (3 - 2 * Math.Clamp(x, 0, 1));
    private double Pick(double a, double b) => a + random.NextDouble() * (b - a);
    private double Side() => random.Next(2) == 0 ? -1 : 1;
}
