using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Atoll.Core.Usage;

/// <summary>Coding-assistant usage providers (the original's ProviderID; the
/// Cursor / Antigravity / New API providers are not ported).</summary>
public enum UsageProviderId { Claude, Codex }

/// <summary>Token and estimated-cost totals for one time window.</summary>
public sealed class UsageTotals
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public double CostUsd { get; set; }
    /// <summary>Some tokens came from a model missing from the price table.</summary>
    public bool HasUnpricedModel { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>A rate-limit window: percent used and when it resets.</summary>
public sealed record UsageLimit(double UsedPercent, DateTimeOffset? ResetsAt)
{
    public double Fraction => Math.Clamp(UsedPercent / 100.0, 0, 1);
}

/// <summary>The original's UsageSnapshot: rolling 5-hour session, calendar
/// today and rolling 7-day week from local logs, plus provider quota windows.</summary>
public sealed class UsageSnapshot
{
    public UsageTotals Session { get; } = new();
    public UsageTotals Today { get; } = new();
    public UsageTotals Week { get; } = new();
    public UsageLimit? SessionLimit { get; set; }
    public UsageLimit? WeekLimit { get; set; }
    /// <summary>Subscription plan label, e.g. "Max 5x" (Claude only).</summary>
    public string? Plan { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
}

public sealed record ProviderUsage(UsageProviderId Provider, UsageSnapshot? Snapshot, string? Error)
{
    public bool IsLoading => Snapshot is null && Error is null;
}

public interface ILlmUsageService
{
    /// <summary>Latest results, one per enabled provider.</summary>
    IReadOnlyList<ProviderUsage> Latest { get; }

    /// <summary>Raised (on a worker thread) when <see cref="Latest"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Re-reads logs and quotas, at most once a minute unless forced.</summary>
    Task RefreshAsync(bool force = false, CancellationToken ct = default);
}
