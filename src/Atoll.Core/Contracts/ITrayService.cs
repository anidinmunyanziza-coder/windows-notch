using System;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Notification-area (tray) icon. Replaces the macOS menu bar
/// (<c>MenuBarLayout</c>, <c>menubarIcon</c>).
/// </summary>
public interface ITrayService : IDisposable
{
    event EventHandler? LeftClicked;
    event EventHandler? RightClicked;

    bool Visible { get; set; }
    string? ToolTip { get; set; }
    Task ShowContextMenuAsync(int screenX, int screenY, CancellationToken ct = default);
}
