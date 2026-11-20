using System;
using System.Runtime.InteropServices;

namespace Atoll.Platform.Windows.Interop;

/// <summary>
/// Win32 / GDI32 / User32 / DwmApi interop for per-monitor floating windows,
/// DPI awareness, and hover tracking. These translate macOS NSScreen /
/// NSWindow / CGEventTap concerns to Windows.
/// </summary>
public static class NativeMethods
{
    // ------------------------------------------------------------------
    // Window styles / extended styles
    // ------------------------------------------------------------------
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_CAPTION = 0x00C00000;
    public const int WS_THICKFRAME = 0x00040000;
    public const int SWP_NOMOVE = 0x0002;
    public const int SWP_NOSIZE = 0x0001;
    public const int SWP_NOZORDER = 0x0004;
    public const int SWP_NOACTIVATE = 0x0010;
    public const int SWP_SHOWWINDOW = 0x0040;
    public const int SWP_FRAMECHANGED = 0x0020;
    public const int HWND_TOPMOST = -1;
    public const int HWND_NOTOPMOST = -2;
    public const int HWND_TOP = 0;

    // ------------------------------------------------------------------
    // Hit testing (WM_NCHITTEST) — used to make the transparent padding
    // around the notch click-through while the notch itself stays live.
    // ------------------------------------------------------------------
    public const int WM_NCHITTEST = 0x0084;
    public const int HTTRANSPARENT = -1;
    public const int HTCLIENT = 1;

    // Horizontal mouse wheel (precision-trackpad left/right scroll).
    public const int WM_MOUSEHWHEEL = 0x020E;

    // Power status change (charger plugged/unplugged, battery level).
    public const int WM_POWERBROADCAST = 0x0218;
    public const int PBT_APMPOWERSTATUSCHANGE = 0x000A;

    // ------------------------------------------------------------------
    // DPI
    // ------------------------------------------------------------------
    public const int MDT_EFFECTIVE_DPI = 0;

    // ------------------------------------------------------------------
    // User32
    // ------------------------------------------------------------------
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(IntPtr hdc, int x, int y);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;        // 0 offline, 1 online, 255 unknown
        public byte BatteryFlag;         // bit flags; 128 = no battery, 8 = charging
        public byte BatteryLifePercent;  // 0-100, 255 unknown
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT tme);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y,
        int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha,
        uint dwFlags);

    public const uint LWA_ALPHA = 0x00000002;

    // ------------------------------------------------------------------
    // Shcore (per-monitor DPI)
    // ------------------------------------------------------------------
    [DllImport("shcore.dll", SetLastError = true)]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX,
        out uint dpiY);

    // ------------------------------------------------------------------
    // DwmApi (blur / acrylic)
    // ------------------------------------------------------------------
    public const int DWMWA_BACKDROP_TYPE = 14;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    public const int DWMWB_MAINMENU = 1;
    public const int DWMWB_TRANSIENTWINDOW = 2;
    public const int DWMWB_TABBEDWINDOW = 3;

    [DllImport("dwmapi.dll", SetLastError = false)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute,
        int cbAttribute);

    // ------------------------------------------------------------------
    // Delegates / structs
    // ------------------------------------------------------------------
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor,
        IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public int cbSize;
        public uint dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    public const uint TME_HOVER = 0x00000001;
    public const uint TME_LEAVE = 0x00000002;
    public const uint HOVER_DEFAULT = 0xFFFFFFFF;
}
