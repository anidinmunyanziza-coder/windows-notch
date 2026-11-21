using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Atoll.Platform.Windows.Interop;

/// <summary>
/// Detects a fullscreen app on a given monitor — the Windows counterpart of the
/// original's <c>FullscreenMediaDetection</c> (MacroVisionKit + AXFullScreen).
/// A window counts as fullscreen when it is the foreground window, covers the
/// monitor's full bounds (taskbar included) and is not merely maximized, which
/// is how video players, browsers in F11 / video fullscreen, and games present.
/// </summary>
public static class FullscreenDetector
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    private static readonly Dictionary<uint, string> _processNames = new();

    /// <summary>The process name (e.g. "chrome", "vlc") of the fullscreen app on
    /// the monitor that holds <paramref name="ownWindow"/>, or null if none.</summary>
    public static string? FullscreenProcessOnMonitorOf(IntPtr ownWindow)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == ownWindow || IsZoomed(fg) || !IsWindowVisible(fg)) return null;

        var cls = new StringBuilder(64);
        GetClassName(fg, cls, cls.Capacity);
        if (ShellClasses.Contains(cls.ToString())) return null;

        var monitor = MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST);
        if (monitor != MonitorFromWindow(ownWindow, MONITOR_DEFAULTTONEAREST)) return null;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(fg, out var r)) return null;
        var m = info.rcMonitor;
        if (r.Left > m.Left || r.Top > m.Top || r.Right < m.Right || r.Bottom < m.Bottom) return null;

        GetWindowThreadProcessId(fg, out var pid);
        if (pid == 0 || pid == Environment.ProcessId) return null;
        return ProcessName(pid);
    }

    /// <summary>Whether a media session's app id (GSMTC SourceAppUserModelId,
    /// e.g. "chrome", "Spotify.exe", "VideoLAN.VLC") belongs to a process name.</summary>
    public static bool MatchesMediaApp(string processName, string? appUserModelId)
    {
        if (string.IsNullOrEmpty(appUserModelId) || string.IsNullOrEmpty(processName)) return false;
        var id = appUserModelId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? appUserModelId[..^4] : appUserModelId;
        return id.Contains(processName, StringComparison.OrdinalIgnoreCase)
            || processName.Contains(id, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ProcessName(uint pid)
    {
        lock (_processNames)
        {
            if (_processNames.TryGetValue(pid, out var cached)) return cached;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                var name = p.ProcessName;
                if (_processNames.Count > 256) _processNames.Clear();
                _processNames[pid] = name;
                return name;
            }
            catch
            {
                return null;
            }
        }
    }

    // ------------------------------------------------------------------
    // Interop
    // ------------------------------------------------------------------

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
