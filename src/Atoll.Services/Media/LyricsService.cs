using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Media;

namespace Atoll.Services.Media;

/// <summary>
/// Synced lyrics from LRCLIB (lrclib.net), the source the original uses: a
/// search by title + artist, the best row picked by <see cref="LyricsMatcher"/>,
/// cached per track for the session. Only the track's title, artist and album
/// are sent.
/// </summary>
public sealed class LyricsService : ILyricsService, IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, LyricsResult> _cache = new();

    public LyricsService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // LRCLIB asks clients to identify the app; nothing about the user is sent.
        if (!_http.DefaultRequestHeaders.UserAgent.TryParseAdd("Atoll-Windows/1.0 (+https://github.com/Ebullioscopic/Atoll)"))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Atoll-Windows/1.0");
    }

    public async Task<LyricsResult> GetAsync(string title, string artist, string album, TimeSpan duration, CancellationToken ct = default)
    {
        var cleanTitle = LyricsMatcher.CleanTitle(title ?? "");
        var cleanArtist = (artist ?? "").Trim();
        if (cleanTitle.Length == 0 || cleanArtist.Length == 0) return LyricsResult.Unavailable;

        var key = $"{LyricsMatcher.Normalize(cleanTitle)}|{LyricsMatcher.Normalize(cleanArtist)}";
        if (_cache.TryGetValue(key, out var cached)) return cached;

        try
        {
            var url = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(Fold(cleanTitle))
                      + "&artist_name=" + Uri.EscapeDataString(Fold(cleanArtist));
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return LyricsResult.Unavailable; // not cached: may be transient

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var rows = JsonSerializer.Deserialize<List<LrclibRecord>>(body, Json) ?? new();
            var result = LyricsMatcher.ToResult(
                LyricsMatcher.BestMatch(rows, cleanArtist, cleanTitle, album ?? "", duration));
            _cache[key] = result;
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return LyricsResult.Unavailable; // offline / malformed: show nothing
        }
    }

    /// <summary>Diacritic-folds the query, as the original does before searching.</summary>
    private static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    public void Dispose() => _http.Dispose();
}
