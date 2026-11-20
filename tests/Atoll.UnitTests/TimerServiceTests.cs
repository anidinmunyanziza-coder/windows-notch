using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Core.Models;
using Atoll.Services.Productivity;
using Xunit;

namespace Atoll.UnitTests;

public class TimerServiceTests
{
    [Fact]
    public async Task TimerService_StartAndStop_TransitionsCorrectly()
    {
        var svc = new TimerService();
        var states = new List<TimerState>();
        svc.StateChanged += (_, s) => states.Add(s);

        await svc.StartAsync(TimeSpan.FromSeconds(2), "test timer");
        var state = svc.State;
        Assert.True(state.IsActive);
        Assert.False(state.IsPaused);
        Assert.Equal("test timer", state.Name);

        await svc.StopAsync();
        state = svc.State;
        Assert.False(state.IsActive);
        Assert.Equal(TimeSpan.Zero, state.Remaining);

        svc.Dispose();
    }

    [Fact]
    public async Task TimerService_PauseAndResume_PreservesRemaining()
    {
        var svc = new TimerService();
        await svc.StartAsync(TimeSpan.FromSeconds(10), "pause test");

        await svc.PauseAsync();
        var paused = svc.State;
        Assert.True(paused.IsPaused);
        Assert.True(paused.Remaining > TimeSpan.Zero);

        await svc.ResumeAsync();
        var resumed = svc.State;
        Assert.False(resumed.IsPaused);
        Assert.True(resumed.Remaining > TimeSpan.Zero);

        await svc.StopAsync();
        svc.Dispose();
    }

    [Fact]
    public void TimerService_Presets_AreNotEmpty()
    {
        var svc = new TimerService();
        Assert.NotEmpty(svc.Presets);
        Assert.Contains(svc.Presets, p => p.Id == "25min");
        svc.Dispose();
    }
}
