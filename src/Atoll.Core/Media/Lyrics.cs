using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Atoll.Core.Media;

/// <summary>One timed lyric line (the original's <c>LyricLine</c>). An empty
/// <see cref="Text"/> marks where singing stops (an instrumental gap).</summary>
public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Track-level lyric availability (the original's <c>LyricsAvailability</c>).</summary>
public enum LyricsAvailability { Loading, Timed, Untimed, Instrumental, Unavailable }

public sealed record LyricsResult(LyricsAvailability Availability, IReadOnlyList<LyricLine> Lines)
{
    public static readonly LyricsResult Loading = new(LyricsAvailability.Loading, Array.Empty<LyricLine>());
    public static readonly LyricsResult Unavailable = new(LyricsAvailability.Unavailable, Array.Empty<LyricLine>());

    /// <summary>Index of the line sung at <paramref name="position"/>, or -1
    /// before the first line.</summary>
    public int IndexAt(TimeSpan position)
    {
        var lo = 0;
        var hi = Lines.Count - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Lines[mid].Time <= position) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found;
    }

    /// <summary>Text sung at <paramref name="position"/> ("" in a gap or before the start).</summary>
    public string LineAt(TimeSpan position)
    {
        var i = IndexAt(position);
        return i < 0 ? "" : Lines[i].Text;
    }
}

/// <summary>Looks up synced lyrics for a track (the original fetches from LRCLIB).</summary>
public interface ILyricsService
{
    Task<LyricsResult> GetAsync(string title, string artist, string album, TimeSpan duration, CancellationToken ct = default);
}

/// <summary>Parses LRC text — a port of the original's <c>LRCParser</c>:
/// <c>[mm:ss.xx]</c> tags (several per line for repeated choruses), the
/// <c>[offset:±ms]</c> tag (positive = earlier), and empty tagged lines kept as gaps.</summary>
public static class LrcParser
{
    private static readonly Regex TimeTag = new(@"\[(\d{1,3}):(\d{2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex OffsetTag = new(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc)) return Array.Empty<LyricLine>();
        var offset = TimeSpan.Zero;
        var om = OffsetTag.Match(lrc);
        if (om.Success && double.TryParse(om.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms))
            offset = TimeSpan.FromMilliseconds(ms);

        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var matches = TimeTag.Matches(line);
            if (matches.Count == 0) continue;
            var last = matches[^1];
            var text = line[(last.Index + last.Length)..].Trim();
            foreach (Match m in matches)
            {
                var minutes = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                var fraction = 0.0;
                if (m.Groups[3].Success)
                {
                    var digits = m.Groups[3].Value; // ".5" is half a second, not 5 hundredths
                    fraction = int.Parse(digits, CultureInfo.InvariantCulture) / Math.Pow(10, digits.Length);
                }
                var t = TimeSpan.FromSeconds(minutes * 60 + seconds + fraction) - offset;
                lines.Add(new LyricLine(t < TimeSpan.Zero ? TimeSpan.Zero : t, text));
            }
        }
        return lines.OrderBy(l => l.Time).ToList();
    }
}

/// <summary>One LRCLIB search row.</summary>
public sealed class LrclibRecord
{
    public string? TrackName { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumName { get; set; }
    public double? Duration { get; set; }
    public bool Instrumental { get; set; }
    public string? PlainLyrics { get; set; }
    public string? SyncedLyrics { get; set; }
}

/// <summary>Picks the right LRCLIB row — a port of the original's
/// <c>LyricsSearchResults</c>: filter to rows agreeing on title and artist (and
/// not an unrequested karaoke / remix / cover version), then prefer rows with
/// synced timings, then the best title / artist / album score.</summary>
public static class LyricsMatcher
{
    private static readonly string[] VersionMarkers =
    {
        "karaoke", "instrumental", "sped up", "spedup", "slowed", "nightcore", "remix",
        "re-record", "rerecord", "'s version", "cover", "tribute", "acapella", "a cappella",
        "made popular by", "originally performed by",
    };

    private static readonly Regex[] TitleCleanup =
    {
        new(@"\s*\((feat\.?|ft\.?|featuring)[^\)]*\)", RegexOptions.IgnoreCase),
        new(@"\s*\[(feat\.?|ft\.?|featuring)[^\]]*\]", RegexOptions.IgnoreCase),
        new(@"\s*-\s*(feat\.?|ft\.?|featuring)\s+.*$", RegexOptions.IgnoreCase),
        new(@"\s*\((remaster(ed)?|live|mono|stereo)[^\)]*\)$", RegexOptions.IgnoreCase),
        new(@"\s*\[(remaster(ed)?|live|mono|stereo)[^\]]*\]$", RegexOptions.IgnoreCase),
    };

    /// <summary>The original's <c>normalizedLyricsTitle</c>: drops "(feat. …)" and
    /// "(Remastered …)" style suffixes before searching.</summary>
    public static string CleanTitle(string title)
    {
        var s = CollapseSpaces(title);
        foreach (var r in TitleCleanup) s = r.Replace(s, "");
        return CollapseSpaces(s);
    }

    public static LrclibRecord? BestMatch(IEnumerable<LrclibRecord> results, string artist, string title, string album, TimeSpan duration)
    {
        artist = Normalize(artist);
        title = Normalize(title);
        album = Normalize(album);
        return results
            .Where(r => AgreesOnTitleAndArtist(r, artist, title)
                        && (!r.Instrumental || IsReliableInstrumental(r, title, duration)))
            .OrderByDescending(HasSynced)
            .ThenByDescending(r => Score(r, artist, title, album))
            .FirstOrDefault();
    }

    public static LyricsResult ToResult(LrclibRecord? record)
    {
        if (record is null) return LyricsResult.Unavailable;
        if (record.Instrumental) return new LyricsResult(LyricsAvailability.Instrumental, Array.Empty<LyricLine>());
        var timed = LrcParser.Parse(record.SyncedLyrics);
        if (timed.Count > 0) return new LyricsResult(LyricsAvailability.Timed, timed);
        return string.IsNullOrWhiteSpace(record.PlainLyrics)
            ? LyricsResult.Unavailable
            : new LyricsResult(LyricsAvailability.Untimed, Array.Empty<LyricLine>());
    }

    private static bool HasSynced(LrclibRecord r) => !string.IsNullOrWhiteSpace(r.SyncedLyrics);

    private static bool AgreesOnTitleAndArtist(LrclibRecord r, string artist, string title)
    {
        var rt = Normalize(r.TrackName);
        return Overlaps(rt, title) && Overlaps(Normalize(r.ArtistName), artist)
               && !VersionMarkers.Any(m => ContainsWord(rt, m) && !ContainsWord(title, m));
    }

    private static bool IsReliableInstrumental(LrclibRecord r, string title, TimeSpan duration)
    {
        if (Normalize(r.TrackName) != title) return false;
        if (duration <= TimeSpan.Zero || r.Duration is not > 0) return true;
        return Math.Abs(r.Duration.Value - duration.TotalSeconds) <= 2;
    }

    private static int Score(LrclibRecord r, string artist, string title, string album)
    {
        var rt = Normalize(r.TrackName);
        var ra = Normalize(r.ArtistName);
        var rb = Normalize(r.AlbumName);
        var score = 0;
        score += rt == title ? 8 : rt.Contains(title) || title.Contains(rt) ? 4 : 0;
        score += ra == artist ? 8 : ra.Contains(artist) || artist.Contains(ra) ? 4 : 0;
        if (album.Length > 0 && rb.Length > 0)
            score += rb == album ? 4 : rb.Contains(album) || album.Contains(rb) ? 2 : 0;
        if (HasSynced(r)) score += 3;
        return score;
    }

    private static bool Overlaps(string a, string b) =>
        a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b) || b.Contains(a));

    private static bool ContainsWord(string haystack, string needle) =>
        Regex.IsMatch(haystack, @"\b" + Regex.Escape(needle), RegexOptions.IgnoreCase);

    /// <summary>Diacritic-folded, lower-cased, trimmed — the original's normalizedForMatching.</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant().Trim();
    }

    private static string CollapseSpaces(string s) => Regex.Replace(s.Trim(), @"\s+", " ");
}
