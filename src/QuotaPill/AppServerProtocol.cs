using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace QuotaPill;

/// <summary>JSON-RPC over caller-owned streams. Has no credential, process or filesystem access.</summary>
public sealed class AppServerProtocol : IDisposable
{
    readonly TextReader input;
    readonly TextWriter output;
    readonly SemaphoreSlim gate = new(1, 1);
    bool initialized;
    long nextId;
    public AppServerProtocol(TextReader input, TextWriter output) { this.input = input; this.output = output; }
    public async Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            if (!initialized)
            {
                await Request("initialize", new { clientInfo = new { name = "codex_quota_pill", title = "Codex Quota Pill", version = "0.1.0" } }, timeout.Token);
                await output.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}".AsMemory(), timeout.Token);
                await output.FlushAsync(timeout.Token);
                initialized = true;
            }
            string result = await Request("account/rateLimits/read", new { }, timeout.Token);
            return QuotaParser.Parse(result, DateTimeOffset.UtcNow);
        }
        finally { gate.Release(); }
    }
    async Task<string> Request(string method, object parameters, CancellationToken ct)
    {
        // Strict allowlist: never create threads, run models, log in, refresh tokens or write configuration.
        if (method is not ("initialize" or "account/rateLimits/read")) throw new InvalidOperationException("不允许的协议方法。");
        long id = ++nextId;
        await output.WriteLineAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }).AsMemory(), ct);
        await output.FlushAsync(ct);
        for (int lines = 0; lines < 256; lines++)
        {
            var line = await input.ReadLineAsync(ct) ?? throw new IOException("额度连接已关闭。");
            if (line.Length > 1024 * 1024) throw new IOException("额度响应超过限制。");
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var responseId)) continue; // notification
            if (root.TryGetProperty("method", out _))
            {
                // A host token/approval request is never fulfilled, even on a future attached transport.
                await output.WriteLineAsync(JsonSerializer.Serialize(new { id = responseId.Clone(), error = new { code = -32601, message = "Read-only quota client" } }).AsMemory(), ct);
                await output.FlushAsync(ct); continue;
            }
            if (responseId.ValueKind != JsonValueKind.Number || !responseId.TryGetInt64(out var receivedId) || receivedId != id) continue;
            if (root.TryGetProperty("error", out _)) throw new IOException("官方额度接口返回错误；未记录响应内容。");
            if (root.TryGetProperty("result", out var result)) return result.GetRawText();
            throw new IOException("额度响应格式无效。");
        }
        throw new IOException("额度连接通知数量超过限制。");
    }
    public void Dispose() => gate.Dispose(); // caller retains stream/process ownership
}
