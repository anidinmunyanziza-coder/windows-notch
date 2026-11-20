using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using Atoll.Core.Contracts;

namespace Atoll.Platform.Windows.Input;

/// <summary>
/// Message-only window that receives WM_HOTKEY notifications. Using
/// <see cref="HwndSource"/> with HWND_MESSAGE avoids creating any visible
/// window while still participating in the global hotkey system.
/// </summary>
public sealed class HotkeyWindow : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public IntPtr Handle => _source.Handle;

    public HotkeyWindow()
    {
        var parameters = new HwndSourceParameters("Atoll.HotkeySink")
        {
            ParentWindow = HwndMessage,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Register a hotkey; returns its numeric ID or throws if taken.</summary>
    public int Register(Key key, ModifierKeys modifiers, Action handler)
    {
        var id = _nextId++;
        var vk = MapKeyToVirtualKey(key);
        var fsModifiers = MapModifiers(modifiers);

        if (!RegisterHotKey(_source.Handle, id, fsModifiers, vk))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"RegisterHotKey failed for {Format(modifiers, key)} (error {error}). " +
                "The combination may already be owned by another application.");
        }

        _handlers[id] = handler;
        return id;
    }

    public void Unregister(int id)
    {
        UnregisterHotKey(_source.Handle, id);
        _handlers.Remove(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handler();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static string Format(ModifierKeys modifiers, Key key) => $"{modifiers}+{key}";

    private static uint MapModifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= 0x0001;      // MOD_ALT
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= 0x0002;  // MOD_CONTROL
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= 0x0004;    // MOD_SHIFT
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= 0x0008;  // MOD_WIN
        return result;
    }

    private static uint MapKeyToVirtualKey(Key key) => key switch
    {
        Key.A => 0x41, Key.B => 0x42, Key.C => 0x43, Key.D => 0x44,
        Key.E => 0x45, Key.F => 0x46, Key.G => 0x47, Key.H => 0x48,
        Key.I => 0x49, Key.J => 0x4A, Key.K => 0x4B, Key.L => 0x4C,
        Key.M => 0x4D, Key.N => 0x4E, Key.O => 0x4F, Key.P => 0x50,
        Key.Q => 0x51, Key.R => 0x52, Key.S => 0x53, Key.T => 0x54,
        Key.U => 0x55, Key.V => 0x56, Key.W => 0x57, Key.X => 0x58,
        Key.Y => 0x59, Key.Z => 0x5A,
        Key.D0 => 0x30, Key.D1 => 0x31, Key.D2 => 0x32, Key.D3 => 0x33,
        Key.D4 => 0x34, Key.D5 => 0x35, Key.D6 => 0x36, Key.D7 => 0x37,
        Key.D8 => 0x38, Key.D9 => 0x39,
        Key.F1 => 0x70, Key.F2 => 0x71, Key.F3 => 0x72, Key.F4 => 0x73,
        Key.F5 => 0x74, Key.F6 => 0x75, Key.F7 => 0x76, Key.F8 => 0x77,
        Key.F9 => 0x78, Key.F10 => 0x79, Key.F11 => 0x7A, Key.F12 => 0x7B,
        Key.Space => 0x20, Key.Escape => 0x1B, Key.Tab => 0x09,
        Key.Enter => 0x0D, Key.Back => 0x08, Key.Delete => 0x2E,
        Key.Up => 0x26, Key.Down => 0x28, Key.Left => 0x25, Key.Right => 0x27,
        Key.Home => 0x24, Key.End => 0x23, Key.PageUp => 0x21,
        Key.PageDown => 0x22, Key.Insert => 0x2D,
        Key.OemComma => 0xBC, Key.OemPeriod => 0xBE,
        Key.OemMinus => 0xBD, Key.OemPlus => 0xBB, Key.OemTilde => 0xC0,
        _ => 0x00,
    };

    public void Dispose()
    {
        foreach (var id in new List<int>(_handlers.Keys))
            Unregister(id);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

/// <summary>
/// Global shortcut service. Translates the macOS shortcut system
/// (<c>KeyboardShortcuts</c> + <c>registerOptionalShortcutHandlers</c>) to
/// Windows <c>RegisterHotKey</c>.
/// </summary>
public sealed class ShortcutService : IShortcutService
{
    private readonly HotkeyWindow _window;
    private readonly Dictionary<int, Shortcut> _idToShortcut = new();
    private readonly Dictionary<string, int> _shortcutToId = new();
    private readonly object _sync = new();

    public event EventHandler<ShortcutInvokedEventArgs>? Invoked;

    public ShortcutService()
    {
        _window = new HotkeyWindow();
    }

    public Task RegisterAsync(Shortcut shortcut, Action handler, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (_shortcutToId.Remove(shortcut.Name, out var existingId))
            {
                _window.Unregister(existingId);
                _idToShortcut.Remove(existingId);
            }

            var id = _window.Register(shortcut.Key, shortcut.Modifiers, () =>
            {
                Invoked?.Invoke(this, new ShortcutInvokedEventArgs
                {
                    Shortcut = shortcut,
                    InvokedAt = DateTimeOffset.UtcNow,
                });
                handler();
            });

            _idToShortcut[id] = shortcut;
            _shortcutToId[shortcut.Name] = id;
        }
        return Task.CompletedTask;
    }

    public Task UnregisterAsync(Shortcut shortcut, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (_shortcutToId.Remove(shortcut.Name, out var id))
            {
                _window.Unregister(id);
                _idToShortcut.Remove(id);
            }
        }
        return Task.CompletedTask;
    }

    public Task<bool> IsRegisteredAsync(Shortcut shortcut)
    {
        lock (_sync)
            return Task.FromResult(_shortcutToId.ContainsKey(shortcut.Name));
    }

    public IReadOnlyList<Shortcut> GetRegistered()
    {
        lock (_sync)
            return new List<Shortcut>(_idToShortcut.Values);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _idToShortcut.Clear();
            _shortcutToId.Clear();
        }
        _window.Dispose();
    }
}
