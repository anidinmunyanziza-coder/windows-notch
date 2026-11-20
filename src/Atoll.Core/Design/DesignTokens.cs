using System;

namespace Atoll.Core.Design;

/// <summary>
/// Visual design tokens translated from Atoll's SwiftUI implementation
/// (see <c>DynamicIsland/ContentView.swift</c>, <c>DynamicIsland/sizing/</c>,
/// <c>DynamicIsland/models/Constants.swift</c>).
/// Pure .NET (no WinUI) so the tokens can be shared by Core, Services and UI.
/// All sizes are in layout pixels at 100% DPI; scale by monitor DPI at runtime.
/// </summary>
public static class DesignTokens
{
    // ------------------------------------------------------------------
    // Notch geometry (from Defaults keys: closedNotchWidth, openNotchWidth,
    // notchHeight, nonNotchHeight)
    // ------------------------------------------------------------------
    public const double ClosedNotchWidth = 170.0;         // compact resting pill when idle
    public const double ClosedMediaNotchWidth = 320.0;    // wider resting pill while media plays (Dynamic Island)
    public const double OpenNotchWidth = 420.0;           // minimalistic open width
    public const double OpenNotchHeight = 205.0;          // room for the header + content (calendar/stats)
    public const double NotchHeight = 32.0;          // notchHeight default
    public const double NonNotchHeight = 32.0;       // nonNotchHeight default
    public const double NotchHorizontalPadding = 12.0;
    public const double NotchTopScreenBleed = 8.0;
    public const double PillTopOffset = 4.0;
    public const double TimerExtraHeight = 250.0;

    // ------------------------------------------------------------------
    // Corner radius (from sizing/matters.swift cornerRadiusInsets)
    // DynamicIslandPillShape: top/bottom radii differ per edge.
    // ------------------------------------------------------------------
    public const double ClosedCornerRadiusTop = 4.0;
    public const double ClosedCornerRadiusBottom = 18.0;
    public const double OpenCornerRadiusTop = 6.0;
    public const double OpenCornerRadiusBottom = 34.0;

    // ------------------------------------------------------------------
    // Shadow (from enableShadow: radius 5 closed / 10 open, opacity 0.6)
    // ------------------------------------------------------------------
    public const double ShadowRadiusClosed = 5.0;
    public const double ShadowRadiusOpen = 10.0;
    public const double ShadowOpacity = 0.6;
    public static readonly (byte A, byte R, byte G, byte B) ShadowColorRgba = (0x99, 0x00, 0x00, 0x00);

    // ------------------------------------------------------------------
    // Animation timings (from ContentView.swift)
    // ------------------------------------------------------------------
    public static readonly TimeSpan HoverAnimationDuration = TimeSpan.FromMilliseconds(250);   // .smooth(duration: 0.25)
    public static readonly TimeSpan ViewTransitionDuration = TimeSpan.FromMilliseconds(300);   // .smooth(duration: 0.3)
    public static readonly TimeSpan SneakPeekHideDuration = TimeSpan.FromMilliseconds(300);    // toggleSneakPeek .smooth(duration: 0.3)
    public const float OpenSpringResponse = 0.42f;     // Animation.spring(response: 0.42, dampingFraction: 1.0)
    public const float OpenSpringDamping = 1.0f;
    public const float CloseSpringResponse = 0.45f;    // close animation response 0.45
    public const float CloseSpringDamping = 1.0f;
    public const float HoverSpringSpeed = 1.2f;        // Animation.bouncy.speed(1.2)
    public const float BlurReplaceDamping = 1.2f;      // .interactiveSpring(dampingFraction: 1.2)

    // Cubic-bezier control points for the "smooth" curve (approximation of
    // SwiftUI's .smooth / .interactiveSpring feel), (x1,y1,x2,y2).
    public static readonly (float X1, float Y1, float X2, float Y2) SmoothEasing = (0.4f, 0.0f, 0.22f, 1.0f);

    // ------------------------------------------------------------------
    // Colors (ARGB bytes)
    // ------------------------------------------------------------------
    public static readonly (byte A, byte R, byte G, byte B) AccentColorDefaultRgba = (0xFF, 0x0A, 0x84, 0xFF); // blue
    public static readonly (byte A, byte R, byte G, byte B) SurfaceColorRgba = (0xFF, 0x00, 0x00, 0x00);       // black
    public static readonly (byte A, byte R, byte G, byte B) PlaceholderColorRgba = (0xFF, 0x1A, 0x1A, 0x1A);
    public static readonly (byte A, byte R, byte G, byte B) TextPrimaryRgba = (0xFF, 0xFF, 0xFF, 0xFF);
    public static readonly (byte A, byte R, byte G, byte B) TextSecondaryRgba = (0xFF, 0x99, 0x99, 0x99);
}
