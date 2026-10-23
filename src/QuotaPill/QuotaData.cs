using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace QuotaPill;
public record QuotaWindow(double Remaining, DateTimeOffset? ResetsAt);
public record QuotaSnapshot(QuotaWindow? FiveHour, QuotaWindow? Weekly, DateTimeOffset FetchedAt);
public static class QuotaParser
{
    public static QuotaSnapshot Parse(string json, DateTimeOffset fetchedAt)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Get(root, "result") is { ValueKind: JsonValueKind.Object } result) root = result;
        JsonElement? bucket = null;
        if (Get(root, "rateLimitsByLimitId") is { ValueKind: JsonValueKind.Object } groups && groups.EnumerateObject().Any())
        {
            if (Get(groups, "codex") is { ValueKind: JsonValueKind.Object } standard) bucket = standard;
            else
            {
                var matches = groups.EnumerateObject().Where(p => Get(p.Value, "limitId") is { ValueKind: JsonValueKind.String } id && id.GetString() == "codex").ToList();
                if (matches.Count == 1) bucket = matches[0].Value;
            }
        }
        else if (Get(root, "rateLimits") is { ValueKind: JsonValueKind.Object } legacy)
        {
            var id = Get(legacy, "limitId");
            if (id is null || id.Value.ValueKind == JsonValueKind.Null || id.Value.ValueKind == JsonValueKind.String && id.Value.GetString() == "codex") bucket = legacy;
        }
        if (bucket is not { } selected) return new(null, null, fetchedAt);
        QuotaWindow? Find(int duration)
        {
            var windows = new List<QuotaWindow>();
            foreach (string field in new[] { "primary", "secondary" })
            {
                if (Get(selected, field) is not { ValueKind: JsonValueKind.Object } w) continue;
                if (Get(w, "windowDurationMins") is not { ValueKind: JsonValueKind.Number } minutes || !minutes.TryGetInt32(out int m) || m != duration) continue;
                if (Get(w, "usedPercent") is not { ValueKind: JsonValueKind.Number } used || !used.TryGetDouble(out double u) || !double.IsFinite(u)) continue;
                DateTimeOffset? reset = null;
                if (Get(w, "resetsAt") is { ValueKind: JsonValueKind.Number } timestamp && timestamp.TryGetInt64(out long seconds) && seconds > 0 && seconds <= 253402300799)
                    reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
                windows.Add(new(Math.Clamp(100 - u, 0, 100), reset));
            }
            return windows.Count == 1 ? windows[0] : null;
        }
        return new(Find(300), Find(10080), fetchedAt);
    }
    static JsonElement? Get(JsonElement obj, string name) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out var value) ? value : null;
}
public static class QuotaFormatting
{
    public static string StatusColor(double? v) => v is null ? "#97A2B2" : v > 50 ? "#9DDBB2" : v >= 20 ? "#E9C781" : v >= 10 ? "#EDA66F" : "#EF7F84";
    public static string Reset(QuotaWindow? window, DateTimeOffset now)
    {
        if (window?.ResetsAt is not { } reset) return "重置 · --";
        if (reset <= now) return "重置 · 待更新";
        long minutes = (long)Math.Ceiling((reset - now).TotalMinutes);
        return minutes >= 60 ? $"重置 · {minutes / 60}h {minutes % 60}m" : $"重置 · {minutes}m";
    }
}
