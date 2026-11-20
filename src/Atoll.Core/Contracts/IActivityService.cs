using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Live-activity / HUD presentation in the closed notch.
/// Translated from <c>components/Live activities/*</c> and the sneak-peek
/// cascade in <c>ContentView.NotchLayout</c>.
/// </summary>
public interface IActivityService : IDisposable
{
    event EventHandler<ActivityShownEventArgs>? ActivityShown;
    event EventHandler<ActivityHiddenEventArgs>? ActivityHidden;

    IReadOnlyList<ActiveActivity> Active { get; }

    Task ShowAsync(Models.SneakContentType type, object? payload = null, double? durationSeconds = null, CancellationToken ct = default);
    Task HideAsync(Models.SneakContentType type, CancellationToken ct = default);
    Task HideAllAsync(CancellationToken ct = default);
}

public sealed class ActivityShownEventArgs : EventArgs
{
    public Models.SneakContentType Type { get; init; }
    public object? Payload { get; init; }
}

public sealed class ActivityHiddenEventArgs : EventArgs
{
    public Models.SneakContentType Type { get; init; }
}

public sealed class ActiveActivity
{
    public Models.SneakContentType Type { get; init; }
    public DateTimeOffset ShownAt { get; init; }
    public double? DurationSeconds { get; init; }
}
