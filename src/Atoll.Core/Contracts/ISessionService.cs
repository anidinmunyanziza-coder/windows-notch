using System;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Session / lock handling. Translated from
/// <c>onScreenLocked</c> / <c>onScreenUnlocked</c> and
/// <c>hideWindowsForLock</c> / <c>restoreWindowsAfterLock</c>.
/// Windows: WTSRegisterSessionNotification (WM_WTSSESSION_CHANGE).
/// </summary>
public interface ISessionService : IDisposable
{
    event EventHandler<SessionChangedEventArgs>? SessionChanged;
    bool IsSessionLocked { get; }
}

public sealed class SessionChangedEventArgs : EventArgs
{
    public bool IsLocked { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
