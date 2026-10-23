using System.Diagnostics;
using System.Text.Json;

internal sealed class MockPeer : IDisposable
{
    public Process Process { get; }
    static readonly List<int> live = new();
    public static bool AllReaped => live.Count == 0;
    MockPeer(Process process) { Process = process; live.Add(process.Id); }
    public static MockPeer Start(string mode)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--mock-server"); start.ArgumentList.Add(mode);
        return new(Process.Start(start)!);
    }
    public void Dispose()
    {
        int id = Process.Id;
        Process.StandardInput.Close();
        if (!Process.WaitForExit(1000)) { Process.Kill(entireProcessTree: false); Process.WaitForExit(3000); }
        if (Process.HasExited) live.Remove(id);
        Process.Dispose();
    }
    public static async Task RunServer(string mode)
    {
        bool initialized = false, hello = false; int reads = 0;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var method)) continue;
            if (method.GetString() == "initialized") { initialized = hello; continue; }
            var id = root.GetProperty("id").GetInt64();
            if (mode == "silent") continue;
            if (method.GetString() == "initialize")
            {
                if (hello) throw new Exception("Duplicate handshake");
                hello = true; Console.WriteLine(JsonSerializer.Serialize(new { id, result = new { userAgent = "mock-test" } })); continue;
            }
            if (method.GetString() != "account/rateLimits/read" || !initialized) throw new Exception("Forbidden method or missing handshake");
            if (mode == "error") { Console.WriteLine(JsonSerializer.Serialize(new { id, error = new { code = 1, message = "TEST_SECRET_MUST_NOT_ESCAPE" } })); continue; }
            if (mode == "auth-request")
            {
                Console.WriteLine("{\"method\":\"account/chatgptAuthTokens/refresh\",\"id\":900,\"params\":{}}");
                using var refused = JsonDocument.Parse((await Console.In.ReadLineAsync())!);
                if (!refused.RootElement.TryGetProperty("error", out _)) throw new Exception("Expected refusal");
            }
            Console.WriteLine("{\"method\":\"test/notification\",\"params\":{}}");
            Console.WriteLine("{\"id\":-100,\"result\":{}}");
            Console.WriteLine(JsonSerializer.Serialize(new { id, result = new { rateLimits = new { primary = new { usedPercent = 57, windowDurationMins = 10080 }, secondary = new { usedPercent = 22 + reads++, windowDurationMins = 300, resetsAt = 1800008040 } } } }));
        }
    }
}
