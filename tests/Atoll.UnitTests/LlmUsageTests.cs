using System;
using System.IO;
using Atoll.Core.Usage;
using Atoll.Services.Usage;
using Xunit;

namespace Atoll.UnitTests;

public class LlmUsageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"atoll_usage_{Guid.NewGuid():N}");

    public LlmUsageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static string ClaudeLine(DateTimeOffset at, string id, int input, int output, int cacheRead = 0, int cacheWrite = 0,
        string model = "claude-sonnet-4-5-20250929") =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + at.UtcDateTime.ToString("O") + "\",\"requestId\":\"req_" + id
        + "\",\"message\":{\"id\":\"msg_" + id + "\",\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":" + input
        + ",\"output_tokens\":" + output + ",\"cache_read_input_tokens\":" + cacheRead
        + ",\"cache_creation_input_tokens\":" + cacheWrite + "}}}";

    [Fact]
    public void ParseLine_Claude_SplitsCacheCounters()
    {
        string? model = null;
        var rec = JsonlUsageParser.ParseLine(ClaudeLine(DateTimeOffset.UtcNow, "a", 10, 20, cacheRead: 300, cacheWrite: 40), ref model);
        Assert.NotNull(rec);
        Assert.Equal(350, rec!.InputTokens);           // uncached + reads + writes
        Assert.Equal(10, rec.UncachedInputTokens);
        Assert.Equal(20, rec.OutputTokens);
        Assert.Equal("msg_a-req_a", rec.DedupKey);
    }

    [Fact]
    public void ParseLine_Codex_UsesModelFromTurnContext()
    {
        string? model = null;
        Assert.Null(JsonlUsageParser.ParseLine("""{"type":"turn_context","payload":{"model":"gpt-5.1-codex"}}""", ref model));
        var rec = JsonlUsageParser.ParseLine(
            """{"timestamp":"2026-11-20T10:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"last_token_usage":{"input_tokens":1000,"cached_input_tokens":600,"output_tokens":50}}}}""",
            ref model);
        Assert.Equal("gpt-5.1-codex", rec?.Model);
        Assert.Equal(1000, rec!.InputTokens);
        Assert.Equal(400, rec.UncachedInputTokens);
    }

    [Fact]
    public void ParseLine_IgnoresNonUsageAndMalformed()
    {
        string? model = null;
        Assert.Null(JsonlUsageParser.ParseLine("""{"type":"user","message":{"content":"hello"}}""", ref model));
        Assert.Null(JsonlUsageParser.ParseLine("""{"usage": broken""", ref model));
    }

    [Fact]
    public void Aggregate_WindowsDedupAndCost()
    {
        var now = DateTimeOffset.Now;
        var file = Path.Combine(_dir, "s.jsonl");
        File.WriteAllLines(file, new[]
        {
            ClaudeLine(now.AddMinutes(-10), "1", 1000, 100),     // session + today + week
            ClaudeLine(now.AddMinutes(-10), "1", 1000, 100),     // duplicate turn: skipped
            ClaudeLine(now.AddDays(-3), "2", 500, 50),           // week only
            ClaudeLine(now.AddDays(-9), "3", 9999, 9999),        // outside the week
            ClaudeLine(now.AddMinutes(-5), "4", 10, 10, model: "totally-unknown-model"),
        });

        var snap = JsonlUsageParser.Aggregate(new[] { file }, now);
        Assert.Equal(1100 + 20, snap.Session.TotalTokens);
        Assert.Equal(1100 + 550 + 20, snap.Week.TotalTokens);
        Assert.True(snap.Week.CostUsd > 0);
        Assert.True(snap.Week.HasUnpricedModel);
    }

    [Theory]
    [InlineData("claude-sonnet-4-5-20250929")]
    [InlineData("claude-haiku-4-5")]
    [InlineData("claude-opus-4-1-20250805")]
    [InlineData("gpt-5.1-codex")]
    [InlineData("gpt-5")]
    public void Pricing_ResolvesLogModelIds(string model) => Assert.NotNull(ModelPricing.Rates(model));

    [Fact]
    public void Pricing_CostUsesCacheRates()
    {
        var r = ModelPricing.Rates("claude-sonnet-4-5")!;
        var cost = ModelPricing.Cost("claude-sonnet-4-5", 1_000_000, 0, 1_000_000, 0)!.Value;
        Assert.Equal(r.Prompt * 1_000_000 + (r.CacheRead ?? r.Prompt) * 1_000_000, cost, 6);
    }

    [Theory]
    [InlineData("default_claude_max_5x", "Max 5x")]
    [InlineData("claude_pro", "Pro")]
    [InlineData("claude_max", "Max")]
    public void PrettyPlan(string raw, string expected) => Assert.Equal(expected, LlmUsageService.PrettyPlan(raw));

    [Fact]
    public void UsageLimit_FractionClamps() =>
        Assert.Equal(1, new UsageLimit(130, null).Fraction);
}
