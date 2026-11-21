using Atoll.Platform.Windows.Interop;
using Xunit;

namespace Atoll.UnitTests;

public class FullscreenDetectorTests
{
    [Theory]
    [InlineData("chrome", "Chrome", true)]                 // Chromium browsers report their name
    [InlineData("msedge", "MSEdge", true)]
    [InlineData("Spotify", "Spotify.exe", true)]           // Win32 apps often report the exe
    [InlineData("vlc", "VideoLAN.VLC", true)]              // dotted app ids contain the process name
    [InlineData("chrome", "Spotify.exe", false)]
    [InlineData("explorer", null, false)]                  // nothing playing
    [InlineData("", "chrome", false)]
    public void MatchesMediaApp(string process, string? appId, bool expected) =>
        Assert.Equal(expected, FullscreenDetector.MatchesMediaApp(process, appId));
}
