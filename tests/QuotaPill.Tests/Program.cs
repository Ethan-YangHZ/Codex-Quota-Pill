using QuotaPill;
using System.IO;

if (args.Contains("--mock-server")) { await MockPeer.RunServer(args.Last()); return; }
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
var now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
var swapped = QuotaParser.Parse("""{"rateLimits":{"primary":{"usedPercent":57,"windowDurationMins":10080},"secondary":{"usedPercent":22,"windowDurationMins":300,"resetsAt":1800008040}}}""", now);
Check("Used percentage is converted to remaining", swapped.FiveHour?.Remaining == 78 && swapped.Weekly?.Remaining == 43);
Check("Reset comes from server timestamp", swapped.FiveHour?.ResetsAt == now.AddMinutes(134));
Check("Reset countdown uses server clock", QuotaFormatting.Reset(swapped.FiveHour, now) == "重置 · 2h 14m");
var grouped = QuotaParser.Parse("""{"rateLimits":{"primary":{"usedPercent":99,"windowDurationMins":300}},"rateLimitsByLimitId":{"codex_other":{"primary":{"usedPercent":1,"windowDurationMins":300}},"codex":{"secondary":{"usedPercent":25,"windowDurationMins":300}}}}""", now);
Check("Codex bucket wins over legacy and other groups", grouped.FiveHour?.Remaining == 75 && grouped.Weekly == null);
var unrelated = QuotaParser.Parse("""{"rateLimitsByLimitId":{"codex_other":{"primary":{"usedPercent":1,"windowDurationMins":300}}}}""", now);
Check("Other bucket never impersonates standard Codex quota", unrelated.FiveHour == null);
var shortPeriod = QuotaParser.Parse("""{"rateLimits":{"primary":{"usedPercent":10,"windowDurationMins":15},"secondary":{"usedPercent":null,"windowDurationMins":10080}}}""", now);
Check("Missing or wrong periods stay missing", shortPeriod.FiveHour == null && shortPeriod.Weekly == null);
Check("Missing reset stays unknown", QuotaFormatting.Reset(grouped.FiveHour, now) == "重置 · --");
Check("Expired reset never implies 100 percent", QuotaFormatting.Reset(swapped.FiveHour, now.AddDays(1)) == "重置 · 待更新" && swapped.FiveHour?.Remaining == 78);
Check("Countdown rounds a partial minute up", QuotaFormatting.Reset(swapped.FiveHour, now.AddSeconds(1)) == "重置 · 2h 14m");
var clamped = QuotaParser.Parse("""{"rateLimits":{"primary":{"usedPercent":140,"windowDurationMins":300,"resetsAt":999999999999999},"secondary":{"usedPercent":-1,"windowDurationMins":10080}}}""", now);
Check("Remaining values clamp to 0 through 100", clamped.FiveHour?.Remaining == 0 && clamped.Weekly?.Remaining == 100);
Check("Invalid Unix seconds are rejected", clamped.FiveHour?.ResetsAt == null);
var duplicate = QuotaParser.Parse("""{"rateLimits":{"primary":{"usedPercent":20,"windowDurationMins":300},"secondary":{"usedPercent":40,"windowDurationMins":300}}}""", now);
Check("Ambiguous duplicate windows stay missing", duplicate.FiveHour == null);
Check("Null result stays missing", QuotaParser.Parse("null", now).FiveHour == null);
bool invalidJson = false; try { QuotaParser.Parse("{", now); } catch (System.Text.Json.JsonException) { invalidJson = true; }
Check("Malformed JSON is rejected", invalidJson);
Console.WriteLine($"{passed} total checks passed.");
using (var peer = MockPeer.Start("normal"))
using (var protocol = new AppServerProtocol(peer.Process.StandardOutput, peer.Process.StandardInput))
{
    var a = await protocol.ReadAsync(CancellationToken.None);
    var b = await protocol.ReadAsync(CancellationToken.None);
    Check("Official handshake and notifications work on owned subprocess", a.FiveHour?.Remaining == 78 && a.Weekly?.Remaining == 43);
    Check("Connection is reused without repeating initialize", b.FiveHour?.Remaining == 77 && peer.Process.HasExited == false);
}
using (var peer = MockPeer.Start("auth-request"))
using (var protocol = new AppServerProtocol(peer.Process.StandardOutput, peer.Process.StandardInput))
{
    Check("Server auth requests are refused", (await protocol.ReadAsync(CancellationToken.None)).FiveHour?.Remaining == 78);
}
using (var peer = MockPeer.Start("error"))
using (var protocol = new AppServerProtocol(peer.Process.StandardOutput, peer.Process.StandardInput))
{
    bool safe = false; try { await protocol.ReadAsync(CancellationToken.None); } catch (IOException e) { safe = !e.Message.Contains("TEST_SECRET"); }
    Check("Server error contents never leak into diagnostics", safe);
}
using (var peer = MockPeer.Start("silent"))
using (var protocol = new AppServerProtocol(peer.Process.StandardOutput, peer.Process.StandardInput))
{
    using var cancel = new CancellationTokenSource(250);
    bool cancelled = false; try { await protocol.ReadAsync(cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check("Unresponsive connection honors cancellation", cancelled);
}
Check("All owned mock subprocesses are reaped", MockPeer.AllReaped);
Console.WriteLine($"{passed} total checks passed.");
