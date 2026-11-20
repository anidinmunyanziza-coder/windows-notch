using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Atoll.Platform.Windows.Interop;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// A full-screen transparent overlay for picking a colour off the screen. Click
/// anywhere to sample the pixel under the cursor; Esc or right-click cancels.
/// The sampled hex is reported via <see cref="Picked"/>.
/// </summary>
public sealed class ColorPickOverlay : Window
{
    public event Action<string>? Picked;

    public ColorPickOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Alpha 1 (not 0) so the window still receives clicks while being, for all
        // practical purposes, invisible — GetPixel then reads the real colour.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Cross;

        // Cover the whole virtual desktop (all monitors).
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        MouseLeftButtonDown += OnPick;
        MouseRightButtonDown += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) => { Activate(); Focus(); };
    }

    private void OnPick(object sender, MouseButtonEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var p)) { Close(); return; }

        var hdc = NativeMethods.GetDC(IntPtr.Zero);
        try
        {
            var colorRef = NativeMethods.GetPixel(hdc, p.X, p.Y);
            byte r = (byte)(colorRef & 0xFF);
            byte g = (byte)((colorRef >> 8) & 0xFF);
            byte b = (byte)((colorRef >> 16) & 0xFF);
            Picked?.Invoke($"#{r:X2}{g:X2}{b:X2}");
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
            Close();
        }
    }
}
