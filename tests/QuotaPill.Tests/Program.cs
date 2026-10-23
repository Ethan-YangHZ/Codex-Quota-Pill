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
var source = new TestSource(); var monitor = new QuotaMonitor(source);
Check("Initial refresh runs", await monitor.RefreshAsync(now) && source.Reads == 1);
Check("Normal refresh is limited to 60 seconds", !await monitor.RefreshAsync(now.AddSeconds(59)) && source.Reads == 1);
Check("Refresh runs at 60 seconds", await monitor.RefreshAsync(now.AddSeconds(60)) && source.Reads == 2);
source.Fail = true;
await monitor.RefreshAsync(now.AddSeconds(120));
Check("Failure preserves cache and marks it stale", monitor.Snapshot != null && monitor.IsStale(now.AddSeconds(120)));
await monitor.RefreshAsync(now.AddSeconds(180));
Check("Repeated failure applies exponential backoff", monitor.NextRefresh == now.AddSeconds(300));
Check("Manual refresh respects failure backoff", !await monitor.RefreshAsync(now.AddSeconds(181), true));
source.Fail = false; await monitor.RefreshAsync(now.AddSeconds(300));
Check("Recovery clears stale error", !monitor.HasError && monitor.NextRefresh == now.AddSeconds(360));
var expirySource = new TestSource { Reset = now.AddSeconds(30) }; var expiring = new QuotaMonitor(expirySource);
await expiring.RefreshAsync(now); await expiring.RefreshAsync(now.AddSeconds(31));
Check("Server reset triggers one early read", expirySource.Reads == 2);
await expiring.RefreshAsync(now.AddSeconds(32));
Check("Expired unchanged timestamp does not cause request storms", expirySource.Reads == 2);
var blocked = new QuotaMonitor(new SafetyBlockedSource());
Check("Safety gate never starts real queries", !await blocked.RefreshAsync(now) && blocked.Snapshot == null);
Check("Color thresholds include 50, 20, 10 exactly", QuotaFormatting.StatusColor(50.1) == "#9DDBB2" && QuotaFormatting.StatusColor(50) == "#E9C781" && QuotaFormatting.StatusColor(20) == "#E9C781" && QuotaFormatting.StatusColor(19.9) == "#EDA66F" && QuotaFormatting.StatusColor(10) == "#EDA66F" && QuotaFormatting.StatusColor(9.9) == "#EF7F84");
Check("Malformed bucket IDs are ignored", QuotaParser.Parse("""{"rateLimitsByLimitId":{"other":{"limitId":123}}}""", now).FiveHour == null);
Check("Legacy non-Codex group is ignored", QuotaParser.Parse("""{"rateLimits":{"limitId":"other","primary":{"usedPercent":1,"windowDurationMins":300}}}""", now).FiveHour == null);
Check("RPC response envelope is accepted", QuotaParser.Parse("""{"id":1,"result":{"rateLimits":{"primary":{"usedPercent":5,"windowDurationMins":300}}}}""", now).FiveHour?.Remaining == 95);
var storageDir = Path.Combine(AppContext.BaseDirectory, "artifacts", "storage-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(storageDir);
var store = new DataStore(storageDir);
store.SavePosition(left);
Check("Dock position and orientation round-trip", store.LoadPosition() == left);
store.SaveQuota(swapped);
Check("Quota cache round-trips without account metadata", store.LoadQuota(now) == swapped && !File.ReadAllText(Path.Combine(storageDir, "quota-cache.json")).Contains("account"));
Check("Expired cache is rejected", store.LoadQuota(now.AddDays(2)) == null);
Check("Future dated cache is rejected", store.LoadQuota(now.AddMinutes(-2)) == null);
File.WriteAllText(Path.Combine(storageDir, "position.json"), "{");
Check("Corrupt position is handled safely", store.LoadPosition() == null && store.LastError != null);
var blockedStore = new DataStore(Path.Combine(storageDir, "position.json", "not-a-directory"));
blockedStore.SavePosition(left);
Check("Read-only/unwritable storage degrades to memory", blockedStore.LastError != null);
Console.WriteLine($"{passed} total checks passed.");
