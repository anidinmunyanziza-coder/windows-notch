using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Atoll.Core.Contracts;
using Atoll.Core.Models;

namespace Atoll.Platform.Windows.Interop;

/// <summary>
/// Enumerates physical monitors and reports DPI + work area per display.
/// Translates macOS NSScreen (per-display geometry, backing scale) to
/// Windows EnumDisplayMonitors / GetDpiForMonitor.
/// </summary>
public static class MonitorHelper
{
    public static IReadOnlyList<DisplayInfo> EnumerateDisplays()
    {
        var displays = new List<DisplayInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
            {
                var mi = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                    return true;

                uint dpiX = 96, dpiY = 96;
                try
                {
                    NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                }
                catch
                {
                    // fall back to 96
                }

                var scaleX = dpiX / 96.0;
                var scaleY = dpiY / 96.0;

                displays.Add(new DisplayInfo
                {
                    Id = hMonitor.ToInt64().ToString("X16"),
                    Name = $"Display {displays.Count + 1}",
                    IsPrimary = (mi.dwFlags & 0x1) != 0,
                    DpiScaleX = scaleX,
                    DpiScaleY = scaleY,
                    Bounds = new Rect
                    {
                        Left = lprcMonitor.Left, Top = lprcMonitor.Top,
                        Right = lprcMonitor.Right, Bottom = lprcMonitor.Bottom
                    },
                    WorkArea = new Rect
                    {
                        Left = mi.rcWork.Left, Top = mi.rcWork.Top,
                        Right = mi.rcWork.Right, Bottom = mi.rcWork.Bottom
                    },
                    IsNotchDevice = false
                });
                return true;
            }, IntPtr.Zero);

        return displays;
    }
}
