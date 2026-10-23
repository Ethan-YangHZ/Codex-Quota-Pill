using QuotaPill;

int passed = 0;
void Check(string name, bool condition) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
var area = new WorkArea(-1920, -120, 0, 960, 1);
var left = Docking.Place(-1910, 500, area);
Check("Left edge uses compact vertical layout", left.Edge == DockEdge.Left && left.Width == 154 && left.Height == 100);
Check("Negative monitor coordinates are retained", left.X == -1914);
var right = Docking.Place(-280, 500, area);
Check("Right edge uses vertical layout", right.Edge == DockEdge.Right && right.X + right.Width <= area.Right);
var top = Docking.Place(-1000, -115, area);
Check("Top stays horizontal", top.Edge == DockEdge.Top && top.Width == 280 && top.Height == 54);
var free = Docking.Place(-1000, 400, area);
Check("Leaving edges restores horizontal", free.Edge == DockEdge.None && free.Width == 280);
var bottom = Docking.Place(-1000, 2000, area);
Check("Bottom is clamped without hidden content", bottom.Y + bottom.Height <= area.Bottom);
var scaled = Docking.Place(0, 200, new WorkArea(0, 0, 3840, 2100, 2));
Check("200 percent DPI scales layout", scaled.Width == 308 && scaled.Height == 200);
Check("Tiny work area clamps size", Docking.Place(0, 0, new WorkArea(0,0,100,70,1)).Width <= 100);
Console.WriteLine($"{passed} docking checks passed.");
