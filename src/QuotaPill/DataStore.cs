using System;
using System.IO;
using System.Text.Json;

namespace QuotaPill;

public sealed class DataStore
{
    readonly string directory;
    public string? LastError { get; private set; }
    public DataStore(string directory) => this.directory = Path.GetFullPath(directory);
    public Placement? LoadPosition() => Read<Placement>("position.json");
    public void SavePosition(Placement p) => Write("position.json", p);
    public QuotaSnapshot? LoadQuota(DateTimeOffset now)
    {
        var q = Read<QuotaSnapshot>("quota-cache.json");
        if (q == null || q.FetchedAt > now.AddMinutes(1) || now - q.FetchedAt > TimeSpan.FromDays(1)) return null;
        bool Valid(QuotaWindow? w) => w == null || double.IsFinite(w.Remaining) && w.Remaining is >= 0 and <= 100;
        return Valid(q.FiveHour) && Valid(q.Weekly) ? q : null;
    }
    public void SaveQuota(QuotaSnapshot q) => Write("quota-cache.json", q);
    T? Read<T>(string name) where T : class
    {
        try
        {
            string file = Path.Combine(directory, name);
            if (!File.Exists(file)) return null;
            if (new FileInfo(file).Length > 64 * 1024) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(file));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { LastError = "本组件本地记录不可读，使用内存状态。"; return null; }
    }
    void Write<T>(string name, T value)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, name), temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, file, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { LastError = "本组件本地记录不可写，使用内存状态。"; }
    }
}
