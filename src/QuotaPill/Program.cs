using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace QuotaPill;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool test = args.Contains("--ui-test");
        using var mutex = new Mutex(true, test ? "Local\\CodexQuotaPill.UiTest.v1" : "Local\\CodexQuotaPill.v1", out bool firstInstance);
        if (!firstInstance) return; // No activation, no messages sent to other processes.
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var window = new PillWindow();
        bool demo = args.Contains("--demo") || args.Contains("--ui-test");
        if (demo) window.SetDisplay(78, 43, "重置 · 2h 14m", "模拟数据 · 仅用于界面测试");
        var before = Native.GetForegroundWindow();
        window.Show();
        var area = Native.AreaAt(0, 0);
        window.MoveTo(Docking.Place(area.Right - 320 * area.Scale, area.Top + 16 * area.Scale, area));
        AppLifetime? lifetime = null;
        if (test)
        {
            string output = System.IO.Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(output);
            int step = 0; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            bool focusPreserved = true;
            timer.Tick += (_, _) =>
            {
                focusPreserved &= Native.GetForegroundWindow() == before;
                if (step == 0) window.SaveRender(System.IO.Path.Combine(output, "horizontal.png"));
                if (step == 1) window.MoveTo(Docking.Place(area.Left, area.Top + 150 * area.Scale, area), true);
                if (step == 2) window.SaveRender(System.IO.Path.Combine(output, "vertical-left.png"));
                if (step == 3) window.MoveTo(Docking.Place(area.Right - 280 * area.Scale, area.Top + 150 * area.Scale, area), true);
                if (step == 4) window.SaveRender(System.IO.Path.Combine(output, "vertical-right.png"));
                if (step == 5) window.MoveTo(Docking.Place(area.Left + 200 * area.Scale, area.Top, area), true);
                if (step == 6)
                {
                    window.SaveRender(System.IO.Path.Combine(output, "top.png"));
                    File.WriteAllText(System.IO.Path.Combine(output, "ui-test.json"), JsonSerializer.Serialize(new { focusPreserved, monitors = Native.Areas(), final = window.CurrentPlacement, scope = "Own window only; rendered WPF content, not a desktop screenshot" }, new JsonSerializerOptions { WriteIndented = true }));
                    timer.Stop(); window.Close();
                }
                step++;
            };
            timer.Start();
        }
        else
        {
            lifetime = new AppLifetime(window, demo);
            app.Dispatcher.BeginInvoke(new Action(async () => await lifetime.Start()));
            if (args.Contains("--integration-test"))
            {
                var smoke = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                smoke.Tick += async (_, _) =>
                {
                    smoke.Stop();
                    bool wasVisible = window.IsVisible;
                    window.Hide(); bool hidden = !window.IsVisible;
                    window.Show();
                    await lifetime.RefreshForTest();
                    string output = System.IO.Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(output);
                    window.SaveRender(System.IO.Path.Combine(output, "unavailable.png"));
                    File.WriteAllText(System.IO.Path.Combine(output, "integration-test.json"), JsonSerializer.Serialize(new
                    { focusPreserved = Native.GetForegroundWindow() == before, wasVisible, hidden, shown = window.IsVisible,
                        tooltip = window.ToolTip?.ToString(), position = window.CurrentPlacement,
                        transportMode = "SafetyBlockedSource (no real transport configured)" }, new JsonSerializerOptions { WriteIndented = true }));
                    window.Close();
                };
                smoke.Start();
            }
        }
        app.Run(window);
        lifetime?.Dispose();
        mutex.ReleaseMutex();
    }
}
