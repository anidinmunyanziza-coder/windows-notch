using System;
using System.Linq;
using Atoll.Core.Media;
using Xunit;

namespace Atoll.UnitTests;

public class LyricsTests
{
    [Fact]
    public void Parse_HandlesFractionsRepeatsGapsAndOffset()
    {
        const string lrc = "[ti:Song]\n[offset:+500]\n[00:01.5]First\n[00:10.25][01:00.25]Chorus\n[00:20.00]\n[00:30]Last";
        var lines = LrcParser.Parse(lrc);

        Assert.Equal(new[] { "First", "Chorus", "", "Last", "Chorus" }, lines.Select(l => l.Text));
        // ".5" is half a second; the +500 ms offset moves every line earlier.
        Assert.Equal(TimeSpan.FromSeconds(1.0), lines[0].Time);
        Assert.Equal(TimeSpan.FromSeconds(9.75), lines[1].Time);
        Assert.Equal(TimeSpan.FromSeconds(59.75), lines[4].Time);
    }

    [Fact]
    public void LineAt_ReturnsTheLineBeingSung()
    {
        var result = new LyricsResult(LyricsAvailability.Timed, LrcParser.Parse("[00:05]One\n[00:10]Two\n[00:15]"));
        Assert.Equal("", result.LineAt(TimeSpan.FromSeconds(2)));    // before the first line
        Assert.Equal("One", result.LineAt(TimeSpan.FromSeconds(7)));
        Assert.Equal("Two", result.LineAt(TimeSpan.FromSeconds(10)));
        Assert.Equal("", result.LineAt(TimeSpan.FromSeconds(16)));   // instrumental gap
    }

    [Fact]
    public void BestMatch_PrefersSyncedRightArtist_AndRejectsUnrequestedVersions()
    {
        var rows = new[]
        {
            new LrclibRecord { TrackName = "Blinding Lights", ArtistName = "Someone Else", SyncedLyrics = "[00:01]x" },
            new LrclibRecord { TrackName = "Blinding Lights (Karaoke Version)", ArtistName = "The Weeknd", SyncedLyrics = "[00:01]k" },
            new LrclibRecord { TrackName = "Blinding Lights", ArtistName = "The Weeknd", PlainLyrics = "plain only" },
            new LrclibRecord { TrackName = "Blinding Lights", ArtistName = "The Weeknd", SyncedLyrics = "[00:01]right" },
        };
        var best = LyricsMatcher.BestMatch(rows, "The Weeknd", "Blinding Lights", "", TimeSpan.Zero);
        Assert.Equal("[00:01]right", best?.SyncedLyrics);
        Assert.Equal(LyricsAvailability.Timed, LyricsMatcher.ToResult(best).Availability);
    }

    [Fact]
    public void BestMatch_IgnoresAccentsAndCase()
    {
        var rows = new[] { new LrclibRecord { TrackName = "Café", ArtistName = "BEYONCÉ", SyncedLyrics = "[00:01]a" } };
        Assert.NotNull(LyricsMatcher.BestMatch(rows, "Beyonce", "cafe", "", TimeSpan.Zero));
    }

    [Theory]
    [InlineData("Song (feat. Someone)", "Song")]
    [InlineData("Song - Remastered 2011", "Song - Remastered 2011")] // only bracketed suffixes are dropped
    [InlineData("Song (Remastered 2011)", "Song")]
    [InlineData("  Two   Spaces ", "Two Spaces")]
    public void CleanTitle(string raw, string expected) => Assert.Equal(expected, LyricsMatcher.CleanTitle(raw));

    [Fact]
    public void ToResult_NullIsUnavailable_InstrumentalIsInstrumental()
    {
        Assert.Equal(LyricsAvailability.Unavailable, LyricsMatcher.ToResult(null).Availability);
        Assert.Equal(LyricsAvailability.Instrumental,
            LyricsMatcher.ToResult(new LrclibRecord { Instrumental = true }).Availability);
    }
}
