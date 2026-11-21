using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Atoll.Core.Usage;

namespace Atoll.Services.Usage;

/// <summary>One assistant turn's token counts.</summary>
public sealed record UsageRecord(
    DateTimeOffset Timestamp, string Model, long InputTokens, long OutputTokens,
    long CacheReadTokens, long CacheWriteTokens, string? DedupKey)
{
    public long UncachedInputTokens => Math.Max(0, InputTokens - CacheReadTokens - CacheWriteTokens);
}

/// <summary>
/// Reads Claude Code (~/.claude/projects/**.jsonl) and Codex
/// (~/.codex/sessions/**.jsonl) transcripts — a port of the original's
/// JSONLUsageParser. Claude lines carry message.usage with separate uncached /
/// cache-write / cache-read counters; Codex logs event_msg/token_count with the
/// model from the preceding turn_context. Duplicated turns are skipped by
/// message id + request id.
/// </summary>
public static class JsonlUsageParser
{
    private const int MaxLineLength = 1024 * 1024;

    public static UsageRecord? ParseLine(string line, ref string? codexModel)
    {
        // Cheap pre-filter: most transcript lines are prompts / tool output.
        if (!line.Contains("\"usage\"") && !line.Contains("token_count") && !line.Contains("turn_context"))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var type = Str(root, "type");
            if (type == "turn_context" && root.TryGetProperty("payload", out var ctx)
                && Str(ctx, "model") is { Length: > 0 } m)
            {
                codexModel = m;
                return null;
            }
            if (type == "event_msg") return ParseCodex(root, codexModel ?? "codex");

            JsonElement message = default;
            var hasMessage = root.TryGetProperty("message", out message) && message.ValueKind == JsonValueKind.Object;
            JsonElement usage;
            if (!(hasMessage && message.TryGetProperty("usage", out usage)) && !root.TryGetProperty("usage", out usage))
                return null;
            if (usage.ValueKind != JsonValueKind.Object) return null;

            var cacheWrite = Int(usage, "cache_creation_input_tokens");
            var cacheRead = Int(usage, "cache_read_input_tokens");
            var input = Int(usage, "input_tokens") + cacheWrite + cacheRead;
            var output = Int(usage, "output_tokens");
            if (input + output <= 0) return null;

            var model = (hasMessage ? Str(message, "model") : null) ?? Str(root, "model") ?? "unknown";
            var ts = Str(root, "timestamp") ?? (hasMessage ? Str(message, "timestamp") : null);
            if (!TryDate(ts, out var when)) return null;

            var messageId = hasMessage ? Str(message, "id") : null;
            var requestId = Str(root, "requestId") ?? Str(root, "request_id");
            var dedup = messageId is null && requestId is null ? null : $"{messageId}-{requestId}";
            return new UsageRecord(when, model, input, output, cacheRead, cacheWrite, dedup);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UsageRecord? ParseCodex(JsonElement root, string model)
    {
        if (!root.TryGetProperty("payload", out var payload) || Str(payload, "type") != "token_count") return null;
        if (!payload.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object) return null;
        if (!info.TryGetProperty("last_token_usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
        if (!TryDate(Str(root, "timestamp"), out var when)) return null;

        // OpenAI semantics: input_tokens already includes cached_input_tokens.
        var input = Int(usage, "input_tokens");
        var output = Int(usage, "output_tokens");
        var cacheRead = Math.Min(input, Int(usage, "cached_input_tokens"));
        var cacheWrite = Int(usage, "cache_write_input_tokens");
        if (input <= 0 && output <= 0) return null;
        return new UsageRecord(when, model, input + cacheWrite, output, cacheRead, cacheWrite, null);
    }

    /// <summary>Totals over the rolling 5 h session, the local calendar day and
    /// the rolling 7 days. Files untouched for a week are skipped unread.</summary>
    public static UsageSnapshot Aggregate(IEnumerable<string> files, DateTimeOffset now)
    {
        var snap = new UsageSnapshot { LastUpdated = now };
        var sessionStart = now.AddHours(-5);
        var weekStart = now.AddDays(-7);
        var today = now.LocalDateTime.Date;
        var seen = new HashSet<string>();

        foreach (var file in files)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < weekStart.UtcDateTime) continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                string? codexModel = null;
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line.Length == 0 || line.Length > MaxLineLength) continue;
                    if (ParseLine(line, ref codexModel) is not { } rec || rec.Timestamp < weekStart) continue;
                    if (rec.DedupKey is { } key && !seen.Add(key)) continue;

                    var cost = ModelPricing.Cost(rec.Model, rec.UncachedInputTokens, rec.OutputTokens,
                        rec.CacheReadTokens, rec.CacheWriteTokens);
                    Add(snap.Week, rec, cost);
                    if (rec.Timestamp.LocalDateTime.Date == today) Add(snap.Today, rec, cost);
                    if (rec.Timestamp >= sessionStart) Add(snap.Session, rec, cost);
                }
            }
            catch (IOException) { /* file in use / vanished: skip */ }
            catch (UnauthorizedAccessException) { }
        }
        return snap;
    }

    private static void Add(UsageTotals t, UsageRecord r, double? cost)
    {
        t.InputTokens += r.InputTokens;
        t.OutputTokens += r.OutputTokens;
        if (cost is { } c) t.CostUsd += c;
        else t.HasUnpricedModel = true;
    }

    private static string? Str(JsonElement o, string name) =>
        o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static long Int(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    private static bool TryDate(string? s, out DateTimeOffset when) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out when);
}
