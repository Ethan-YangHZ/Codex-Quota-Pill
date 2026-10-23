using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace QuotaPill;

internal sealed class PillWindow : Window
{
    readonly Border surface = new();
    readonly Grid content = new();
    readonly StackPanel five = new(), week = new();
    readonly TextBlock fiveValue = new(), weekValue = new(), reset = new();
    readonly Border separator = new();
    readonly DispatcherTimer animation = new() { Interval = TimeSpan.FromMilliseconds(16) };
    IntPtr hwnd;
    bool dragging, moving;
    int offsetX, offsetY;
    DateTime animationStart;
    Placement? animationFrom, animationTo;
    public Placement? CurrentPlacement { get; private set; }
    public event Action? PositionChanged;

    public PillWindow()
    {
        Title = "Codex Quota Pill"; Width = 280; Height = 54;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent; Topmost = true; ShowActivated = false;
        ShowInTaskbar = false; Focusable = false; UseLayoutRounding = true;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        surface.CornerRadius = new CornerRadius(27);
        surface.BorderThickness = new Thickness(1);
        surface.BorderBrush = new SolidColorBrush(Color.FromArgb(78, 175, 187, 204));
        surface.Background = new LinearGradientBrush(Color.FromArgb(213, 35, 39, 47), Color.FromArgb(232, 20, 23, 29), 90);
        surface.Child = content; Content = surface;
        content.Children.Add(five); content.Children.Add(separator); content.Children.Add(week);
        five.Children.Add(ValueRow("clock", "5h", fiveValue));
        reset.FontSize = 10; reset.Foreground = new SolidColorBrush(Color.FromRgb(166, 175, 189));
        reset.Margin = new Thickness(24, 1, 0, 0); five.Children.Add(reset);
        week.Children.Add(ValueRow("calendar", "7d", weekValue));
        separator.Background = new SolidColorBrush(Color.FromArgb(43, 189, 201, 218));
        ApplyLayout(false);
        SetDisplay(null, null, "重置 · --", "未连接额度来源");
        SourceInitialized += (_, _) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.Configure(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
        };
        MouseLeftButtonDown += BeginDrag;
        MouseMove += Drag;
        MouseLeftButtonUp += EndDrag;
        LostMouseCapture += (_, _) => { if (dragging) FinishDrag(); };
        animation.Tick += Animate;
        Closed += (_, _) => animation.Stop();
    }

    static FrameworkElement ValueRow(string icon, string label, TextBlock value)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(icon == "clock" ? "M10,2 A8,8 0 1 1 9.99,2 M10,5 L10,10 13,12"
                : "M3,4 L17,4 17,18 3,18 Z M3,8 L17,8 M6,1 L6,6 M14,1 L14,6"),
            Stroke = new SolidColorBrush(Color.FromRgb(173, 186, 200)), StrokeThickness = 1.4,
            Width = 18, Height = 18, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 7, 0)
        };
        row.Children.Add(path);
        row.Children.Add(new TextBlock { Text = label, FontSize = 15, Foreground = new SolidColorBrush(Color.FromRgb(215, 222, 232)), Margin = new Thickness(0, 0, 7, 0) });
        value.FontSize = 17; value.FontWeight = FontWeights.SemiBold;
        row.Children.Add(value); return row;
    }

    internal void SetDisplay(double? fiveRemaining, double? weeklyRemaining, string resetText, string tip, bool stale = false)
    {
        void Set(TextBlock block, double? number)
        {
            block.Text = number.HasValue ? $"{number.Value:0}%" : "--";
            block.Foreground = new SolidColorBrush(ColorFor(number));
        }
        Set(fiveValue, fiveRemaining); Set(weekValue, weeklyRemaining);
        reset.Text = resetText;
        if (stale) { if (fiveRemaining.HasValue) fiveValue.Text += "*"; if (weeklyRemaining.HasValue) weekValue.Text += "*"; }
        ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(this,
            $"Codex 额度，5 小时剩余 {fiveValue.Text}，每周剩余 {weekValue.Text}，{resetText}，{tip}");
    }
    static Color ColorFor(double? v) => (Color)ColorConverter.ConvertFromString(QuotaFormatting.StatusColor(v));

    void ApplyLayout(bool vertical)
    {
        content.RowDefinitions.Clear(); content.ColumnDefinitions.Clear();
        five.Margin = week.Margin = new Thickness(0);
        five.VerticalAlignment = week.VerticalAlignment = VerticalAlignment.Center;
        if (vertical)
        {
            surface.CornerRadius = new CornerRadius(13); content.Margin = new Thickness(13, 7, 13, 7);
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            Grid.SetRow(five, 0); Grid.SetRow(separator, 1); Grid.SetRow(week, 2);
            Grid.SetColumn(five, 0); Grid.SetColumn(separator, 0); Grid.SetColumn(week, 0);
            separator.Width = double.NaN; separator.Height = 1; separator.Margin = new Thickness(1, 0, 1, 0);
        }
        else
        {
            surface.CornerRadius = new CornerRadius(27); content.Margin = new Thickness(16, 6, 16, 6);
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(138) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(five, 0); Grid.SetRow(separator, 0); Grid.SetRow(week, 0);
            Grid.SetColumn(five, 0); Grid.SetColumn(separator, 1); Grid.SetColumn(week, 2);
            week.Margin = new Thickness(13, 0, 0, 0);
            separator.Width = 1; separator.Height = 26; separator.Margin = new Thickness(0);
        }
    }

    internal void MoveTo(Placement p, bool animate = false)
    {
        if (animate && animation.IsEnabled && animationTo?.Edge == p.Edge) { animationTo = p; return; }
        animation.Stop();
        bool vertical = p.Edge is DockEdge.Left or DockEdge.Right;
        ApplyLayout(vertical);
        if (animate && CurrentPlacement is { } old && (old.X != p.X || old.Y != p.Y))
        {
            animationFrom = old; animationTo = p; animationStart = DateTime.UtcNow; animation.Start();
        }
        else SetBounds(p);
    }
    void SetBounds(Placement p)
    {
        moving = true;
        Native.SetWindowPos(hwnd, new IntPtr(-1), p.X, p.Y, p.Width, p.Height, Native.NoActivate);
        Native.Round(hwnd, p.Width, p.Height, p.Edge is DockEdge.Left or DockEdge.Right, Native.Area(Native.MonitorFromWindow(hwnd, 2)).Scale);
        CurrentPlacement = p; moving = false;
    }
    void Animate(object? sender, EventArgs e)
    {
        if (animationFrom is not { } from || animationTo is not { } to) return;
        double t = Math.Clamp((DateTime.UtcNow - animationStart).TotalMilliseconds / 140, 0, 1);
        double eased = 1 - Math.Pow(1 - t, 3);
        // Keep the full final-size capsule inside the target work area for the whole animation.
        var area = Native.AreaAt(to.X + to.Width / 2, to.Y + to.Height / 2);
        int x = (int)Math.Round(from.X + (to.X - from.X) * eased);
        int y = (int)Math.Round(from.Y + (to.Y - from.Y) * eased);
        SetBounds(to with { X = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - to.Width)),
            Y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - to.Height)) });
        if (t >= 1) { animation.Stop(); PositionChanged?.Invoke(); }
    }
    void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        animation.Stop(); Native.GetCursorPos(out var cursor); Native.GetWindowRect(hwnd, out var r);
        offsetX = cursor.X - r.Left; offsetY = cursor.Y - r.Top;
        dragging = true; CaptureMouse(); e.Handled = true;
    }
    void Drag(object sender, MouseEventArgs e)
    {
        if (!dragging) return;
        Native.GetCursorPos(out var cursor);
        var area = Native.AreaAt(cursor.X, cursor.Y);
        // Dock proximity always uses the horizontal footprint, independent of the current orientation.
        var p = Docking.Place(cursor.X - offsetX, cursor.Y - offsetY, area);
        MoveTo(p, CurrentPlacement?.Edge != p.Edge || animation.IsEnabled); e.Handled = true;
    }
    void EndDrag(object sender, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false; ReleaseMouseCapture(); FinishDrag(); e.Handled = true;
    }
    void FinishDrag()
    {
        dragging = false;
        if (CurrentPlacement is { } p)
        {
            var area = Native.AreaAt(p.X + p.Width / 2, p.Y + p.Height / 2);
            double hx = p.Edge == DockEdge.Right ? area.Right - 280 * area.Scale : p.X;
            MoveTo(Docking.Place(hx, p.Y, area), true);
        }
        PositionChanged?.Invoke();
    }
    IntPtr WindowMessage(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE, still accept mouse
        if (msg is 0x2E0 or 0x7E && !moving) // DPI / display topology
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (dragging || CurrentPlacement is not { } p) return;
                var area = Native.Area(Native.MonitorFromWindow(hwnd, 2));
                double x = p.Edge == DockEdge.Left ? area.Left : p.Edge == DockEdge.Right ? area.Right - 280 * area.Scale : p.X;
                MoveTo(Docking.Place(x, p.Edge == DockEdge.Top ? area.Top : p.Y, area));
                PositionChanged?.Invoke();
            }));
        }
        return IntPtr.Zero;
    }
    internal void SaveRender(string path)
    {
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * 2), (int)Math.Ceiling(surface.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}
