using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Atoll.Platform.Windows.Animation;

/// <summary>
/// Analytic step response of a damped harmonic oscillator, used to reproduce
/// SwiftUI's <c>Animation.spring(response:dampingFraction:)</c> inside a WPF
/// <see cref="DoubleAnimation"/>.
///
/// For a critically damped spring (dampingFraction = 1.0) the normalized step
/// response is <c>1 - (1 + 2πt)e^(-2πt)</c>, where t is elapsed time divided
/// by the spring's response. This is the exact curve SwiftUI integrates, not
/// an eyeballed bezier.
/// </summary>
public sealed class SpringEasing : EasingFunctionBase
{
    /// <summary>Damping ratio (SwiftUI dampingFraction). 1.0 = critically damped.</summary>
    public double DampingFraction { get; set; } = 1.0;

    protected override double EaseInCore(double normalizedTime)
    {
        return SpringMath.Progress(normalizedTime, DampingFraction);
    }

    protected override Freezable CreateInstanceCore() => new SpringEasing
    {
        DampingFraction = DampingFraction,
        EasingMode = EasingMode,
    };
}

public static class SpringMath
{
    private const double TwoPi = 2.0 * Math.PI;

    /// <summary>
    /// Normalized progress of a spring at normalized time t (0..1), where the
    /// animation duration equals the spring response.
    /// </summary>
    public static double Progress(double t, double dampingFraction = 1.0)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;

        var zeta = Math.Clamp(dampingFraction, 0.05, 4.0);

        if (Math.Abs(zeta - 1.0) < 1e-4)
        {
            // Critically damped: 1 - (1 + ωt)e^(-ωt), ωt = 2πt.
            return 1.0 - (1.0 + TwoPi * t) * Math.Exp(-TwoPi * t);
        }

        if (zeta < 1.0)
        {
            // Underdamped.
            var wd = TwoPi * Math.Sqrt(1.0 - zeta * zeta);
            var decay = Math.Exp(-zeta * TwoPi * t);
            return 1.0 - decay * (Math.Cos(wd * t) + (zeta * TwoPi / wd) * Math.Sin(wd * t));
        }

        // Overdamped.
        var root = Math.Sqrt(zeta * zeta - 1.0);
        var r1 = -TwoPi * (zeta - root);
        var r2 = -TwoPi * (zeta + root);
        return 1.0 - (r2 * Math.Exp(r1 * t) - r1 * Math.Exp(r2 * t)) / (r2 - r1);
    }

    public static double Lerp(double from, double to, double progress)
        => from + (to - from) * progress;
}
