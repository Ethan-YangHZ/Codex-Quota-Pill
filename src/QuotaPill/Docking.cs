using System;

namespace QuotaPill;

public enum DockEdge { None, Top, Left, Right }
public record WorkArea(int Left, int Top, int Right, int Bottom, double Scale);
public record Placement(int X, int Y, int Width, int Height, DockEdge Edge);
public static class Docking
{
    public static Placement Place(double x, double y, WorkArea area)
    {
        double s = Math.Clamp(area.Scale, .5, 4);
        int gap = (int)Math.Round(6 * s), threshold = (int)Math.Round(24 * s);
        double dl = Math.Abs(x - area.Left), dr = Math.Abs(x + 280 * s - area.Right);
        DockEdge edge = dl <= threshold && dl <= dr ? DockEdge.Left
            : dr <= threshold ? DockEdge.Right
            : Math.Abs(y - area.Top) <= threshold ? DockEdge.Top : DockEdge.None;
        int w = Math.Min((int)Math.Round((edge is DockEdge.Left or DockEdge.Right ? 154 : 280) * s), area.Right - area.Left);
        int h = Math.Min((int)Math.Round((edge is DockEdge.Left or DockEdge.Right ? 100 : 54) * s), area.Bottom - area.Top);
        int px = edge == DockEdge.Left ? area.Left + gap : edge == DockEdge.Right ? area.Right - w - gap : (int)Math.Round(x);
        int py = edge == DockEdge.Top ? area.Top + gap : (int)Math.Round(y);
        return new(Math.Clamp(px, area.Left, Math.Max(area.Left, area.Right - w)),
            Math.Clamp(py, area.Top, Math.Max(area.Top, area.Bottom - h)), w, h, edge);
    }
}
