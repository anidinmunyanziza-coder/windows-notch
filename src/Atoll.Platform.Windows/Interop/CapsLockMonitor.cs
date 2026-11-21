using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Atoll.Platform.Windows.Interop;

/// <summary>
/// Tracks the Caps Lock toggle — the Windows counterpart of the original's
/// <c>CapsLockManager</c> (which uses global flagsChanged monitors).
///
/// GetKeyState alone is not enough: a thread's key-state table only updates as
/// that thread reads keyboard messages, so a background notch would see a stale
/// value. Instead this listens to raw keyboard input (RIDEV_INPUTSINK, delivered
/// even when the app is in the background) on a message-only window, and on a
/// Caps Lock press reads the true toggle through the foreground thread's input
/// state (AttachThreadInput + GetKeyState). Must be created on the UI thread.
/// </summary>
public sealed class CapsLockMonitor : IDisposable
{
    private static CapsLockMonitor? _shared;

    /// <summary>The shared monitor; first access must be on the UI thread.</summary>
    public static CapsLockMonitor Shared => _shared ??= new CapsLockMonitor();

    /// <summary>Raised on the UI thread when Caps Lock turns on or off.</summary>
    public event Action<bool>? Changed;

    public bool IsOn { get; private set; }

    private readonly HwndSource? _source;
    private readonly DispatcherTimer _settle;
    private bool _keyDown;

    private CapsLockMonitor()
    {
        IsOn = ReadToggle();

        // A Caps Lock press is processed asynchronously; read the result a beat later.
        _settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            Refresh();
        };

        try
        {
            _source = new HwndSource(new HwndSourceParameters("AtollCapsLockSink")
            {
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
                WindowStyle = 0,
            });
            _source.AddHook(WndProc);

            var device = new RAWINPUTDEVICE
            {
                UsagePage = 0x01, // generic desktop
                Usage = 0x06,     // keyboard
                Flags = RIDEV_INPUTSINK,
                Target = _source.Handle,
            };
            if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            {
                _source.Dispose();
                _source = null;
            }
        }
        catch
        {
            _source = null;
        }
    }

    /// <summary>Re-reads the real toggle state and raises <see cref="Changed"/> if it moved.</summary>
    public void Refresh()
    {
        var on = ReadToggle();
        if (on == IsOn) return;
        IsOn = on;
        Changed?.Invoke(on);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT) return IntPtr.Zero;

        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, headerSize);
        if (size == 0 || size > 256) return IntPtr.Zero;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size) return IntPtr.Zero;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(buffer);
            if (header.Type != RIM_TYPEKEYBOARD) return IntPtr.Zero;

            // RAWKEYBOARD: MakeCode(2) Flags(2) Reserved(2) VKey(2) Message(4) Extra(4)
            var flags = (ushort)Marshal.ReadInt16(buffer, (int)headerSize + 2);
            var vkey = (ushort)Marshal.ReadInt16(buffer, (int)headerSize + 6);
            if (vkey != VK_CAPITAL) return IntPtr.Zero;

            var isBreak = (flags & RI_KEY_BREAK) != 0;
            if (!isBreak && !_keyDown)
            {
                _keyDown = true; // ignore auto-repeat while held
                _settle.Stop();
                _settle.Start();
            }
            else if (isBreak)
            {
                _keyDown = false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return IntPtr.Zero;
    }

    /// <summary>The system-wide Caps Lock toggle, read through the foreground
    /// thread's input state so it is never stale.</summary>
    private static bool ReadToggle()
    {
        var me = GetCurrentThreadId();
        var fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
        if (fg == 0 || fg == me) return (GetKeyState(VK_CAPITAL) & 1) != 0;

        if (!AttachThreadInput(me, fg, true)) return (GetKeyState(VK_CAPITAL) & 1) != 0;
        try { return (GetKeyState(VK_CAPITAL) & 1) != 0; }
        finally { AttachThreadInput(me, fg, false); }
    }

    public void Dispose()
    {
        _settle.Stop();
        _source?.Dispose();
    }

    // ------------------------------------------------------------------
    // Interop
    // ------------------------------------------------------------------

    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const uint RIM_TYPEKEYBOARD = 1;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const ushort RI_KEY_BREAK = 0x01;
    private const int VK_CAPITAL = 0x14;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool doAttach);
}
