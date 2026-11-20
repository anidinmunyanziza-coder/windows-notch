using System.Runtime.InteropServices;

namespace Atoll.Platform.Windows.Input;

/// <summary>Screen-space cursor position (physical pixels).</summary>
public static class CursorPosition
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    public static bool TryGet(out int x, out int y)
    {
        if (GetCursorPos(out var pt))
        {
            x = pt.X;
            y = pt.Y;
            return true;
        }
        x = y = 0;
        return false;
    }
}
