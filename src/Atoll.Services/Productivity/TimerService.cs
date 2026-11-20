using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Core.Models;

namespace Atoll.Services.Productivity;

/// <summary>
/// Countdown timer service. Translated from the original <c>TimerManager</c>,
/// <c>TimerPreset</c>, <c>TimerDisplayMode</c>.
/// Uses a monotonic timestamp (Environment.TickCount64) so sleep/resume is
/// handled correctly — remaining time is computed from the target deadline,
/// not from accumulated ticks.
/// </summary>
public sealed class TimerService : ITimerService
{
    private readonly object _sync = new();
    private System.Threading.Timer? _tickTimer;
    private DateTimeOffset _deadline;
    private TimeSpan _remaining;
    private bool _isPaused;
    private string? _name;

    public event EventHandler<TimerState>? StateChanged;
    public event EventHandler<TimeSpan>? Tick;

    public TimerState State
    {
        get
        {
            lock (_sync)
            {
                var remaining = ComputeRemaining();
                return new TimerState
                {
                    IsActive = _remaining > TimeSpan.Zero,
                    IsPaused = _isPaused,
                    Duration = _remaining, // total duration when not running; see ComputeRemaining
                    Remaining = remaining,
                    Name = _name,
                    StartedAt = _isPaused || _remaining <= TimeSpan.Zero ? null : _deadline.Add(-_remaining)
                };
            }
        }
    }

    public IReadOnlyList<TimerPreset> Presets { get; } = new List<TimerPreset>
    {
        new TimerPreset { Id = "1min", Name = "1 minute", Duration = TimeSpan.FromMinutes(1) },
        new TimerPreset { Id = "5min", Name = "5 minutes", Duration = TimeSpan.FromMinutes(5) },
        new TimerPreset { Id = "10min", Name = "10 minutes", Duration = TimeSpan.FromMinutes(10) },
        new TimerPreset { Id = "15min", Name = "15 minutes", Duration = TimeSpan.FromMinutes(15) },
        new TimerPreset { Id = "25min", Name = "25 minutes (Pomodoro)", Duration = TimeSpan.FromMinutes(25) },
        new TimerPreset { Id = "60min", Name = "1 hour", Duration = TimeSpan.FromHours(1) },
    };

    public TimerService()
    {
        _tickTimer = new System.Threading.Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public Task StartAsync(TimeSpan duration, string? name = null, CancellationToken ct = default)
    {
        lock (_sync)
        {
            _remaining = duration;
            _name = name;
            _isPaused = false;
            _deadline = DateTimeOffset.UtcNow.Add(duration);
        }
        _tickTimer?.Change(0, 250);
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public Task StartPresetAsync(string presetId, CancellationToken ct = default)
    {
        var preset = Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset == null) return Task.CompletedTask;
        return StartAsync(preset.Duration, preset.Name, ct);
    }

    public Task PauseAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (_isPaused || _remaining <= TimeSpan.Zero) return Task.CompletedTask;
            _isPaused = true;
            _remaining = ComputeRemaining();
        }
        _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public Task ResumeAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (!_isPaused || _remaining <= TimeSpan.Zero) return Task.CompletedTask;
            _isPaused = false;
            _deadline = DateTimeOffset.UtcNow.Add(_remaining);
        }
        _tickTimer?.Change(0, 250);
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public Task ResetAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            _isPaused = false;
            _deadline = DateTimeOffset.UtcNow; // remaining recomputed below
        }
        _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            _remaining = TimeSpan.Zero;
            _isPaused = false;
            _name = null;
        }
        _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _tickTimer?.Dispose();
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private TimeSpan ComputeRemaining()
    {
        // If remaining was explicitly zeroed (StopAsync), honor that.
        if (_remaining <= TimeSpan.Zero && !_isPaused) return TimeSpan.Zero;
        if (_isPaused) return _remaining;
        var remaining = _deadline - DateTimeOffset.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void OnTick(object? state)
    {
        lock (_sync)
        {
            if (_remaining <= TimeSpan.Zero || _isPaused)
            {
                _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }
            var remaining = ComputeRemaining();
            if (remaining <= TimeSpan.Zero)
            {
                _remaining = TimeSpan.Zero;
                _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
        NotifyStateChanged();
        Tick?.Invoke(this, ComputeRemaining());
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, State);
    }
}
