using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace QuotaPill;

internal sealed class AppLifetime : IDisposable
{
    readonly PillWindow window;
    readonly DataStore store;
    readonly QuotaMonitor monitor;
    readonly bool demo;
    readonly CancellationTokenSource stop = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Forms.NotifyIcon tray;
    bool disposed;
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    internal AppLifetime(PillWindow window, bool demo)
    {
        this.window = window; this.demo = demo;
        store = new DataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        IQuotaSource source = demo ? new DemoSource() : new SafetyBlockedSource();
        // The released safety-blocked build never loads old account data or starts Codex.
        monitor = new QuotaMonitor(source);
        tray = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "Codex Quota Pill", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        var show = menu.Items.Add("隐藏");
        show.Click += (_, _) =>
        {
            if (window.IsVisible) window.Hide(); else window.Show();
            show.Text = window.IsVisible ? "隐藏" : "显示";
        };
        menu.Items.Add("刷新", null, async (_, _) => await Refresh(true));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => window.Close());
        tray.ContextMenuStrip = menu;
        window.PositionChanged += SavePosition;
        window.Closed += (_, _) => Dispose();
        timer.Tick += async (_, _) => await Refresh(false);
    }
    internal async Task Start()
    {
        var saved = store.LoadPosition();
        var area = saved == null ? Native.AreaAt(0, 0) : Native.AreaAt(saved.X + saved.Width / 2, saved.Y + saved.Height / 2);
        double x = saved == null ? area.Right - 320 * area.Scale : saved.Edge == DockEdge.Left ? area.Left
            : saved.Edge == DockEdge.Right ? area.Right - 280 * area.Scale : saved.X;
        double y = saved == null ? area.Top + 16 * area.Scale : saved.Edge == DockEdge.Top ? area.Top : saved.Y;
        window.MoveTo(Docking.Place(x, y, area));
        await Refresh(false); timer.Start();
    }
    async Task Refresh(bool manual)
    {
        if (disposed) return;
        var now = DateTimeOffset.UtcNow;
        bool changed = await monitor.RefreshAsync(now, manual, stop.Token);
        if (disposed) return;
        if (changed && !demo && monitor.Snapshot is { } q) store.SaveQuota(q);
        var data = monitor.Snapshot;
        bool stale = monitor.IsStale(now);
        string tip = monitor.Status;
        if (stale) tip += "\n* 数据可能不是最新的，尚未确认重置。";
        if (data != null) tip += "\n更新时间：" + data.FetchedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        if (data?.FiveHour?.ResetsAt is { } reset) tip += "\n预计重置：" + reset.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz");
        if (store.LastError != null) tip += "\n" + store.LastError;
        window.SetDisplay(data?.FiveHour?.Remaining, data?.Weekly?.Remaining, QuotaFormatting.Reset(data?.FiveHour, now), tip, stale);
        tray.Text = demo ? "Codex Quota Pill · 模拟数据" : data == null ? "Codex Quota Pill · 真实额度未连接（安全隔离）" : "Codex Quota Pill";
    }
    internal Task RefreshForTest() => Refresh(true);
    void SavePosition() { if (window.CurrentPlacement is { } p) store.SavePosition(p); }
    static Icon MakeIcon()
    {
        using var bitmap = new Bitmap(32, 32); using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var pen = new Pen(Color.FromArgb(181, 214, 199), 2.5f);
        graphics.DrawEllipse(pen, 4, 4, 24, 24); graphics.DrawLine(pen, 16, 9, 16, 16); graphics.DrawLine(pen, 16, 16, 21, 19);
        IntPtr hicon = bitmap.GetHicon();
        try { using var source = Icon.FromHandle(hicon); return (Icon)source.Clone(); }
        finally { DestroyIcon(hicon); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        timer.Stop(); stop.Cancel(); SavePosition();
        tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); stop.Dispose();
    }
}
