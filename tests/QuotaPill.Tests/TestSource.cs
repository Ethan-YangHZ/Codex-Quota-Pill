using QuotaPill;
using System.IO;
internal sealed class TestSource : IQuotaSource
{
    public bool IsBlocked => false;
    public bool IsDemo => false;
    public bool Fail { get; set; }
    public int Reads { get; private set; }
    public DateTimeOffset? Reset { get; set; }
    public Task<QuotaSnapshot> ReadAsync(CancellationToken ct)
    {
        Reads++;
        if (Fail) throw new IOException("mock offline");
        return Task.FromResult(new QuotaSnapshot(new(78, Reset), null, DateTimeOffset.UtcNow));
    }
}
