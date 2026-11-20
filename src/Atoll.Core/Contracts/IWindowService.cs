using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Atoll.Core.Models;
using Atoll.Core.ViewModels;

namespace Atoll.Core.Contracts;

/// <summary>
/// Per-monitor floating notch windows. Translated from
/// <c>DynamicIslandApp.swift</c> window creation/positioning
/// (<c>createDynamicIslandWindow</c>, <c>positionWindow</c>,
/// <c>ensureWindowSize</c>, <c>adjustWindowPosition</c>) and
/// <c>MenuBarLayout</c> / <c>NotchSpaceManager</c>.
/// </summary>
public interface IWindowService : IDisposable
{
    event EventHandler<DisplayChangedEventArgs>? DisplayConfigurationChanged;
    event EventHandler<WindowStateChangedEventArgs>? NotchWindowStateChanged;

    IReadOnlyList<DisplayInfo> Displays { get; }

    /// <summary>Show/hide the notch window on a display.</summary>
    Task SetNotchVisibleAsync(string displayId, bool visible, CancellationToken ct = default);

    /// <summary>Set notch state (closed -> open resizes the window).</summary>
    Task SetNotchStateAsync(string displayId, Models.NotchState state, CancellationToken ct = default);

    /// <summary>Position the notch at the top edge of a display.</summary>
    Task PositionNotchAsync(string displayId, CancellationToken ct = default);

    /// <summary>Close all notch windows (shutdown / lock screen).</summary>
    Task HideAllAsync(CancellationToken ct = default);
    Task RestoreAllAsync(CancellationToken ct = default);

    /// <summary>Get the view model for a display's notch (for UI content binding).</summary>
    NotchViewModel? GetViewModel(string displayId);
}

public sealed class DisplayInfo
{
    public string Id { get; init; } = string.Empty;
    public string? Name { get; init; }
    public bool IsPrimary { get; init; }
    public double DpiScaleX { get; init; } = 1.0;
    public double DpiScaleY { get; init; } = 1.0;
    public Rect Bounds { get; init; } = new();
    public Rect WorkArea { get; init; } = new();
    public bool IsNotchDevice { get; init; } // always false on Windows; kept for parity
}

public sealed class Rect
{
    public double Left, Top, Right, Bottom;
    public double Width => Right - Left;
    public double Height => Bottom - Top;
}

public sealed class DisplayChangedEventArgs : EventArgs { public IReadOnlyList<DisplayInfo> Displays { get; init; } = null!; }
public sealed class WindowStateChangedEventArgs : EventArgs { public string DisplayId { get; init; } = string.Empty; public Models.NotchState State { get; init; } }
