using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace QuotaPill;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] internal struct MonitorInfo
    { public int Size; public Rect Monitor, Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
    internal const uint NoActivate = 0x10, NoZOrder = 0x4;
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int width, int height);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    internal delegate bool MonitorEnum(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rect, MonitorEnum callback, IntPtr data);

    internal static WorkArea AreaAt(int x, int y) => Area(MonitorFromPoint(new Point { X = x, Y = y }, 2));
    internal static WorkArea Area(IntPtr monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("无法读取显示器工作区。");
        GetDpiForMonitor(monitor, 0, out var dpi, out _);
        return new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom, dpi > 0 ? dpi / 96d : 1);
    }
    internal static List<WorkArea> Areas()
    {
        var areas = new List<WorkArea>();
        MonitorEnum callback = (IntPtr m, IntPtr dc, ref Rect r, IntPtr data) => { areas.Add(Area(m)); return true; };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        return areas;
    }
    internal static void Configure(IntPtr hwnd)
    {
        long styles = GetWindowLongPtr(hwnd, -20).ToInt64();
        SetWindowLongPtr(hwnd, -20, new IntPtr(styles | 0x08000000L | 0x80L)); // WS_EX_NOACTIVATE | TOOLWINDOW
        var margins = new Margins { Left = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int dark = 1, acrylic = 3, corner = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        DwmSetWindowAttribute(hwnd, 33, ref corner, sizeof(int));
        DwmSetWindowAttribute(hwnd, 38, ref acrylic, sizeof(int)); // documented Windows 11 transient backdrop
    }
    internal static void Round(IntPtr hwnd, int width, int height, bool vertical, double scale)
    {
        int radius = (int)Math.Round((vertical ? 26 : 54) * scale);
        IntPtr region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
        if (SetWindowRgn(hwnd, region, true) == 0) DeleteObject(region); // OS owns region on success
    }
}
