using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Global keyboard shortcuts. Translated from <c>KeyboardShortcuts</c> usage
/// and <c>registerOptionalShortcutHandlers</c>. Windows: RegisterHotKey.
/// </summary>
public interface IShortcutService : IDisposable
{
    event EventHandler<ShortcutInvokedEventArgs>? Invoked;

    Task RegisterAsync(Shortcut shortcut, Action handler, CancellationToken ct = default);
    Task UnregisterAsync(Shortcut shortcut, CancellationToken ct = default);
    Task<bool> IsRegisteredAsync(Shortcut shortcut);
    IReadOnlyList<Shortcut> GetRegistered();
}

public sealed class Shortcut
{
    public Key Key { get; init; }
    public ModifierKeys Modifiers { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsGlobal { get; init; } = true;
}

public enum Key { None = 0, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Space, Escape, Tab, Enter, Back, Delete, Up, Down, Left, Right,
    Home, End, PageUp, PageDown, Insert, OemComma, OemPeriod, OemMinus, OemPlus, OemTilde }

[Flags]
public enum ModifierKeys { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }

public sealed class ShortcutInvokedEventArgs : EventArgs
{
    public Shortcut Shortcut { get; init; } = null!;
    public DateTimeOffset InvokedAt { get; init; } = DateTimeOffset.UtcNow;
}
