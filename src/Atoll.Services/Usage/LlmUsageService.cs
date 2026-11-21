using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Core.Usage;

namespace Atoll.Services.Usage;

/// <summary>
/// Coding-assistant usage for the LLM tab — a port of the original's
/// LLMUsageManager with its Claude and Codex providers. Token counts and
/// API-equivalent cost come from the tools' local transcripts. Rate-limit
/// windows come from each service's usage endpoint using the tool's own saved
/// sign-in, read-only: an expired token is skipped rather than refreshed, so
/// this never rotates or rewrites the tools' credentials.
/// </summary>
public sealed class LlmUsageService : ILlmUsageService, IDisposable
{
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(60);

    private readonly ISettingsService _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;
    private IReadOnlyList<ProviderUsage> _latest = Array.Empty<ProviderUsage>();

    public event EventHandler? Changed;

    public LlmUsageService(ISettingsService settings) => _settings = settings;

    public IReadOnlyList<ProviderUsage> Latest => _latest;

    private IEnumerable<UsageProviderId> EnabledProviders()
    {
        if (_settings.Get("enableClaudeProvider", true)) yield return UsageProviderId.Claude;
        if (_settings.Get("enableCodexProvider", true)) yield return UsageProviderId.Codex;
    }

    public async Task RefreshAsync(bool force = false, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return; // a refresh is already running
        try
        {
            var now = DateTimeOffset.Now;
            if (!force && now - _lastRefresh < MinRefreshInterval && _latest.Count > 0) return;
            _lastRefresh = now;

            var providers = EnabledProviders().ToList();
            // Keep earlier results visible while reloading; show loading for new ones.
            _latest = providers.Select(p => _latest.FirstOrDefault(r => r.Provider == p) ?? new ProviderUsage(p, null, null)).ToList();
            Changed?.Invoke(this, EventArgs.Empty);

            var results = await Task.WhenAll(providers.Select(p => Task.Run(() => FetchAsync(p, now, ct), ct)));
            _latest = results;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ProviderUsage> FetchAsync(UsageProviderId provider, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            return provider switch
            {
                UsageProviderId.Claude => await FetchClaudeAsync(now, ct),
                _ => await FetchCodexAsync(now, ct),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProviderUsage(provider, null, ex.Message);
        }
    }

    // ------------------------------------------------------------------
    // Claude Code
    // ------------------------------------------------------------------

    private async Task<ProviderUsage> FetchClaudeAsync(DateTimeOffset now, CancellationToken ct)
    {
        var root = Path.Combine(_home, ".claude", "projects");
        if (!Directory.Exists(root))
            return new ProviderUsage(UsageProviderId.Claude, null, "Claude Code not detected");
        var files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).ToList();
        if (files.Count == 0)
            return new ProviderUsage(UsageProviderId.Claude, null, "No Claude usage logs found");

        var snap = JsonlUsageParser.Aggregate(files, now);
        snap.Plan = ReadClaudePlan();
        (snap.SessionLimit, snap.WeekLimit) = await FetchClaudeLimitsAsync(ct);
        return new ProviderUsage(UsageProviderId.Claude, snap, null);
    }

    /// <summary>Plan label from the non-secret oauthAccount fields in ~/.claude.json.</summary>
    private string? ReadClaudePlan()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_home, ".claude.json")));
            if (!doc.RootElement.TryGetProperty("oauthAccount", out var account)) return null;
            foreach (var field in new[] { "organizationRateLimitTier", "organizationType" })
                if (account.TryGetProperty(field, out var v) && v.GetString()?.Trim() is { Length: > 0 } raw)
                    return PrettyPlan(raw);
        }
        catch { }
        return null;
    }

    /// <summary>"default_claude_max_5x" → "Max 5x"; "claude_pro" → "Pro".</summary>
    public static string PrettyPlan(string raw)
    {
        var s = raw;
        foreach (var prefix in new[] { "default_claude_", "claude_", "default_" })
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { s = s[prefix.Length..]; break; }
        return string.Join(" ", s.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(seg => Regex.IsMatch(seg, "^[0-9]+x$") ? seg : char.ToUpperInvariant(seg[0]) + seg[1..]));
    }

    private async Task<(UsageLimit?, UsageLimit?)> FetchClaudeLimitsAsync(CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(_home, ".claude", ".credentials.json");
            if (!File.Exists(path)) return (null, null);
            using var creds = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
            if (!creds.RootElement.TryGetProperty("claudeAiOauth", out var oauth)) return (null, null);
            var token = oauth.TryGetProperty("accessToken", out var t) ? t.GetString() : null;
            var expiresMs = oauth.TryGetProperty("expiresAt", out var e) && e.TryGetInt64(out var ms) ? ms : 0;
            // Expired: Claude Code refreshes it next time it runs; never refresh here.
            if (string.IsNullOrEmpty(token) || DateTimeOffset.FromUnixTimeMilliseconds(expiresMs) <= DateTimeOffset.UtcNow)
                return (null, null);

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
            request.Headers.TryAddWithoutValidation("User-Agent", "Atoll-Windows/1.0");
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return (null, null);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return (ClaudeWindow(body.RootElement, "five_hour"), ClaudeWindow(body.RootElement, "seven_day"));
        }
        catch
        {
            return (null, null);
        }
    }

    private static UsageLimit? ClaudeWindow(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        if (!w.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) return null;
        DateTimeOffset? resets = null;
        if (w.TryGetProperty("resets_at", out var r))
        {
            if (r.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(r.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d))
                resets = d;
            else if (r.ValueKind == JsonValueKind.Number)
                resets = DateTimeOffset.FromUnixTimeMilliseconds((long)r.GetDouble());
        }
        return new UsageLimit(u.GetDouble(), resets);
    }

    // ------------------------------------------------------------------
    // Codex
    // ------------------------------------------------------------------

    private string CodexHome()
    {
        var env = Environment.GetEnvironmentVariable("CODEX_HOME")?.Trim();
        return string.IsNullOrEmpty(env) ? Path.Combine(_home, ".codex") : env;
    }

    private async Task<ProviderUsage> FetchCodexAsync(DateTimeOffset now, CancellationToken ct)
    {
        var root = Path.Combine(CodexHome(), "sessions");
        if (!Directory.Exists(root))
            return new ProviderUsage(UsageProviderId.Codex, null, "Codex not detected");
        var files = Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).ToList();
        if (files.Count == 0)
            return new ProviderUsage(UsageProviderId.Codex, null, "No Codex usage logs found");

        var snap = JsonlUsageParser.Aggregate(files, now);
        (snap.SessionLimit, snap.WeekLimit) = await FetchCodexLimitsAsync(ct);
        return new ProviderUsage(UsageProviderId.Codex, snap, null);
    }

    private async Task<(UsageLimit?, UsageLimit?)> FetchCodexLimitsAsync(CancellationToken ct)
    {
        try
        {
            string? token = null, accountId = null;
            foreach (var path in new[] { Path.Combine(CodexHome(), "auth.json"), Path.Combine(_home, ".config", "codex", "auth.json") })
            {
                if (!File.Exists(path)) continue;
                using var auth = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
                if (!auth.RootElement.TryGetProperty("tokens", out var tokens)) continue;
                token = tokens.TryGetProperty("access_token", out var t) ? t.GetString() : null;
                accountId = tokens.TryGetProperty("account_id", out var a) ? a.GetString() : null;
                if (!string.IsNullOrEmpty(token)) break;
            }
            if (string.IsNullOrEmpty(token)) return (null, null);

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("User-Agent", "Atoll");
            if (!string.IsNullOrEmpty(accountId)) request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return (null, null);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!body.RootElement.TryGetProperty("rate_limit", out var rl)) return (null, null);
            return (CodexWindow(rl, "primary_window"), CodexWindow(rl, "secondary_window"));
        }
        catch
        {
            return (null, null);
        }
    }

    private static UsageLimit? CodexWindow(JsonElement rl, string name)
    {
        if (!rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        if (!w.TryGetProperty("used_percent", out var u) || u.ValueKind != JsonValueKind.Number) return null;
        DateTimeOffset? resets = null;
        if (w.TryGetProperty("reset_at", out var at) && at.ValueKind == JsonValueKind.Number)
            resets = DateTimeOffset.FromUnixTimeSeconds((long)at.GetDouble());
        else if (w.TryGetProperty("reset_after_seconds", out var after) && after.ValueKind == JsonValueKind.Number)
            resets = DateTimeOffset.Now.AddSeconds(after.GetDouble());
        return new UsageLimit(u.GetDouble(), resets);
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
