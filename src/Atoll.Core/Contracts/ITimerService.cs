using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Countdown timer. Translated from <c>TimerManager</c>,
/// <c>TimerPreset</c>, <c>TimerDisplayMode</c>.
/// </summary>
public interface ITimerService : IDisposable
{
    event EventHandler<TimerState>? StateChanged;
    event EventHandler<TimeSpan>? Tick;

    TimerState State { get; }
    IReadOnlyList<TimerPreset> Presets { get; }

    Task StartAsync(TimeSpan duration, string? name = null, CancellationToken ct = default);
    Task StartPresetAsync(string presetId, CancellationToken ct = default);
    Task PauseAsync(CancellationToken ct = default);
    Task ResumeAsync(CancellationToken ct = default);
    Task ResetAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

public sealed class TimerState
{
    public bool IsActive { get; init; }
    public bool IsPaused { get; init; }
    public TimeSpan Duration { get; init; }
    public TimeSpan Remaining { get; init; }
    public string? Name { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
}

public sealed class TimerPreset
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public string? Color { get; init; }
}
