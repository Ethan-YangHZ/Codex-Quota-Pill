using System;
using System.Threading;
using System.Threading.Tasks;

namespace QuotaPill;
public interface IQuotaSource
{
    bool IsBlocked { get; }
    bool IsDemo { get; }
    Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken);
}
public sealed class SafetyBlockedSource : IQuotaSource
{
    public bool IsBlocked => true;
    public bool IsDemo => false;
    public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("真实接入因共享状态写入风险而封锁。");
}
internal sealed class DemoSource : IQuotaSource
{
    readonly DateTimeOffset reset = DateTimeOffset.UtcNow.AddMinutes(134);
    public bool IsBlocked => false;
    public bool IsDemo => true;
    public Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(new QuotaSnapshot(new(78, reset), new(43, DateTimeOffset.UtcNow.AddDays(5)), DateTimeOffset.UtcNow));
}
public sealed class QuotaMonitor
{
    readonly IQuotaSource source;
    int busy, failures;
    DateTimeOffset? handledReset, lastAttempt;
    public QuotaSnapshot? Snapshot { get; private set; }
    public DateTimeOffset NextRefresh { get; private set; } = DateTimeOffset.MinValue;
    public bool HasError { get; private set; }
    public string Status => source.IsDemo ? "模拟数据 · 非真实额度" : source.IsBlocked
        ? "真实额度未连接：安全隔离已阻止启动 Codex。" : HasError ? "读取失败，缓存数据可能不是最新的。" : "官方额度，只读查询。";
    public bool IsStale(DateTimeOffset now) => Snapshot != null && (HasError || now - Snapshot.FetchedAt > TimeSpan.FromMinutes(3)
        || Snapshot.FetchedAt > now.AddMinutes(1) || Snapshot.FiveHour?.ResetsAt <= now);
    public QuotaMonitor(IQuotaSource source, QuotaSnapshot? cached = null) { this.source = source; Snapshot = cached; HasError = cached != null; }
    public async Task<bool> RefreshAsync(DateTimeOffset now, bool manual = false, CancellationToken cancellationToken = default)
    {
        if (source.IsBlocked || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;
        try
        {
            if (Snapshot?.FiveHour?.ResetsAt is { } reset && reset <= now && handledReset != reset)
            { handledReset = reset; NextRefresh = now; }
            if (now < NextRefresh && (!manual || failures > 0 || lastAttempt > now.AddSeconds(-10))) return false;
            lastAttempt = now;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                Snapshot = await source.ReadAsync(timeout.Token).WaitAsync(timeout.Token);
                HasError = false; failures = 0; NextRefresh = now.AddSeconds(60);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return false; }
            catch (Exception e) when (e is System.IO.IOException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {
                HasError = true; failures = Math.Min(failures + 1, 5);
                NextRefresh = now.AddSeconds(Math.Min(900, 60 * Math.Pow(2, failures - 1)));
                return false;
            }
        }
        finally { Volatile.Write(ref busy, 0); }
    }
}
