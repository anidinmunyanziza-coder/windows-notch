using System.Collections.Generic;
using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Core.Models;
using Atoll.Services.Media;
using Xunit;

namespace Atoll.UnitTests;

public class MediaServiceTests
{
    [Fact]
    public async Task MediaService_StartStop_DoesNotThrow()
    {
        var svc = new MediaService();
        await svc.StartMonitoringAsync();
        Assert.True(svc.IsMonitoring);
        await svc.StopMonitoringAsync();
        Assert.False(svc.IsMonitoring);
    }

    [Fact]
    public void MediaService_CurrentState_DefaultIsIdle()
    {
        var svc = new MediaService();
        Assert.True(svc.CurrentState.IsPlayerIdle);
        Assert.False(svc.CurrentState.IsPlaying);
    }

    [Fact]
    public async Task MediaService_GetSessions_ReturnsList()
    {
        var svc = new MediaService();
        await svc.StartMonitoringAsync();
        var sessions = await svc.GetSessionsAsync();
        Assert.NotNull(sessions);
        // May be empty if no media player is running - that's valid.
        await svc.StopMonitoringAsync();
    }

    [Fact]
    public async Task MediaService_SendCommand_WithNoSession_ReturnsFalse()
    {
        var svc = new MediaService();
        // Don't start monitoring - no session available.
        var result = await svc.SendCommandAsync(MediaCommand.PlayPause);
        Assert.False(result);
    }

    [Fact]
    public void MediaPlaybackState_Record_With_SupportsCopy()
    {
        var original = new MediaPlaybackState
        {
            Title = "Original",
            Artist = "Artist",
            IsPlaying = true
        };
        var copy = original with { Title = "Changed" };
        Assert.Equal("Original", original.Title);
        Assert.Equal("Changed", copy.Title);
        Assert.True(copy.IsPlaying);
    }
}
