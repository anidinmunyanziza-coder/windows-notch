using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Atoll.Core.Media;
using Atoll.Core.Models;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Live lyrics as a detached "satellite" capsule floating beside the closed
/// notch, in the spirit of the iPhone's split Dynamic Island. While a track
/// with synced lyrics plays it splits off the notch's trailing edge — a
/// liquid neck stretches, thins and snaps — then rests a few points away,
/// resizing to each line and sweeping it from white into the album colour as
/// it is sung. When playback pauses or stops, the notch opens, or the line
/// source goes away, it slides back, the neck re-forms, and the notch
/// swallows it.
///
/// Everything is one Path rebuilt per frame (capsule ∪ neck) drawn behind the
/// notch, driven by two springs: <c>_sep</c> (0 = absorbed, 1 = detached) and
/// the capsule width.
/// </summary>
public partial class NotchWindow
{
    private const double SatGap = 12;          // rest distance from the notch
    private const double SatNeckLength = 11;   // neck snaps beyond this gap
    private const double SatMaxWidth = 300;
    private const double SatPadX = 14;
    private const double SatFontSize = 12.5;

    /// <summary>Wide enough for the open notch, or the widest closed pill plus a
    /// fully detached capsule on either side (the margin is click-through).</summary>
    private double WindowWidth =>
        Math.Max(_vm.OpenWidth, DesignTokens_ClosedMax + 2 * (SatGap + SatMaxWidth + 24)) + ShadowPad * 2;

    /// <summary>Widest resting closed pill (the volume HUD).</summary>
    private double DesignTokens_ClosedMax =>
        Math.Max(Atoll.Core.Design.DesignTokens.ClosedMediaNotchWidth, ClosedHudActivity.PillWidth(
            SneakContentType.Volume, 1, new HudOptions(), _vm.ClosedWidth));

    private Path? _satPath;
    private Canvas? _satTextHost;
    private TextBlock? _satTextA, _satTextB;
    private Path? _satNote;
    private bool _satFrontA = true;
    private LinearGradientBrush? _satSweep;
    private bool _satRendering;
    private TimeSpan _satLastFrame;

    // springs
    private double _sep, _sepV, _sepTarget;
    private double _satW, _satWV, _satWTarget;

    private string? _satLine;
    private int _satLineIndex = -1;
    private byte[]? _satArt;
    private Color _satTint = Color.FromRgb(0x8E, 0x8E, 0x93);

    private void InitLyricsCapsule()
    {
        _satPath = new Path
        {
            Fill = Brushes.Black,
            Effect = new DropShadowEffect { Color = Colors.Black, Opacity = 0.6, BlurRadius = 10, ShadowDepth = 0 },
        };
        _satSweep = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        _satTextA = SatText();
        _satTextB = SatText();
        _satNote = new Path
        {
            Data = Geometry.Parse("M 6.4,17.2 A 2.9,2.4 0 1 0 12.2,17.2 A 2.9,2.4 0 1 0 6.4,17.2 Z M 12.2,17.2 V 4 C 14,5.6 16.6,6.2 18,8.6"),
            Fill = Brushes.White, Stroke = Brushes.White, StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 10, Height = 12, Stretch = Stretch.Uniform, Opacity = 0,
        };
        _satTextHost = new Canvas { Children = { _satTextA, _satTextB, _satNote } };
        SatelliteLayer.Children.Add(_satPath);
        SatelliteLayer.Children.Add(_satTextHost);

        _satW = _satWTarget = _vm.ClosedHeight;
        EnsureSatelliteRendering();
    }

    private static TextBlock SatText() => new()
    {
        FontSize = SatFontSize,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Opacity = 0,
    };

    private void EnsureSatelliteRendering()
    {
        if (_satRendering || _satPath is null) return;
        _satRendering = true;
        _satLastFrame = TimeSpan.Zero;
        System.Windows.Media.CompositionTarget.Rendering += OnSatelliteFrame;
    }

    private void StopSatelliteRendering()
    {
        if (!_satRendering) return;
        _satRendering = false;
        System.Windows.Media.CompositionTarget.Rendering -= OnSatelliteFrame;
    }

    /// <summary>Called on state / media / lyrics changes to wake the loop.</summary>
    private void PokeLyricsCapsule() => EnsureSatelliteRendering();

    private void OnSatelliteFrame(object? sender, EventArgs e)
    {
        if (_satPath is null || _isClosing) return;
        var now = ((RenderingEventArgs)e).RenderingTime;
        var dt = _satLastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _satLastFrame).TotalSeconds, 0, 1 / 20.0);
        _satLastFrame = now;
        if (dt <= 0) return;

        // ── what should be showing ──────────────────────────────────────
        var media = _shell.MediaState;
        var lyrics = _shell.Lyrics;
        var position = TimeSpan.Zero;
        if (media is not null)
        {
            position = media.Position;
            if (media.IsPlaying) position += TimeSpan.FromTicks((long)((DateTimeOffset.UtcNow - media.UpdatedAt).Ticks * media.PlaybackRate));
        }

        var show = _vm.State != NotchState.Open
                   && !_hiddenForFullscreen
                   && media is { IsPlaying: true, IsAdvertisement: false }
                   && lyrics.Availability == LyricsAvailability.Timed
                   && _shell.GetSetting("enableLyrics", true);

        var index = show ? lyrics.IndexAt(position) : -1;
        var line = index >= 0 ? lyrics.Lines[index].Text.Trim() : "";
        if (show) SetSatelliteLine(line, index);
        _sepTarget = show ? 1 : 0;

        // ── springs ─────────────────────────────────────────────────────
        // Splitting off is a touch bouncier than being swallowed.
        var (resp, damp) = _sepTarget > _sep ? (0.55, 0.72) : (0.42, 0.9);
        Spring(ref _sep, ref _sepV, _sepTarget, resp, damp, dt);
        Spring(ref _satW, ref _satWV, _satWTarget, 0.42, 0.86, dt);

        // Karaoke sweep through the current line.
        if (show && index >= 0 && line.Length > 0)
        {
            var start = lyrics.Lines[index].Time;
            var end = index + 1 < lyrics.Lines.Count ? lyrics.Lines[index + 1].Time : start + TimeSpan.FromSeconds(4);
            var span = Math.Max(0.4, (end - start).TotalSeconds * 0.85); // finish a little before the next line
            UpdateSweep(Math.Clamp((position - start).TotalSeconds / span, 0, 1));
        }

        LayoutSatellite();

        // Sleep once fully absorbed and at rest.
        if (_sepTarget == 0 && _sep < 0.001 && Math.Abs(_sepV) < 0.001)
        {
            _sep = 0;
            _satPath.Data = null;
            _satTextHost!.Visibility = Visibility.Collapsed;
            if (!ShouldWatchLyrics()) StopSatelliteRendering();
        }
    }

    /// <summary>Keep the loop alive (cheaply) while music plays so the capsule can
    /// appear the moment a timed line starts.</summary>
    private bool ShouldWatchLyrics() =>
        _shell.MediaState is { IsPlaying: true } && _shell.Lyrics.Availability is LyricsAvailability.Timed or LyricsAvailability.Loading;

    private void SetSatelliteLine(string line, int index)
    {
        if (line == _satLine && index == _satLineIndex) return;
        var changed = line != _satLine;
        _satLine = line;
        _satLineIndex = index;
        if (!changed) return;

        var h = _vm.ClosedHeight;
        if (line.Length == 0)
        {
            // Instrumental gap: shrink to a circle holding a note.
            _satWTarget = h;
        }
        else
        {
            var ft = new FormattedText(line, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(_satTextA!.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                SatFontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            _satWTarget = Math.Clamp(ft.WidthIncludingTrailingWhitespace + SatPadX * 2, h * 1.6, SatMaxWidth);
        }

        // Blur-replace the text: the old line drifts up and fades, the new one
        // rises in from below.
        var incoming = _satFrontA ? _satTextB! : _satTextA!;
        var outgoing = _satFrontA ? _satTextA! : _satTextB!;
        _satFrontA = !_satFrontA;
        incoming.Text = line;
        incoming.Foreground = _satSweep;
        outgoing.Foreground = Brushes.White;

        var dur = TimeSpan.FromMilliseconds(280);
        var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        FadeSat(incoming, 0, 1, 7, 0, dur, ease);
        FadeSat(outgoing, outgoing.Opacity, 0, 0, -7, dur, ease);
        _satNote!.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(line.Length == 0 ? 1 : 0, dur));

        // Album tint for the not-yet-sung part of the line.
        var art = _shell.MediaState?.ArtworkBytes;
        if (!ReferenceEquals(art, _satArt))
        {
            _satArt = art;
            var avg = AlbumPalette.Average(art);
            _satTint = art is { Length: > 0 } ? Brighten(avg, 0.62) : Color.FromRgb(0x8E, 0x8E, 0x93);
        }
        UpdateSweep(0);
    }

    private static void FadeSat(TextBlock tb, double fromOp, double toOp, double fromY, double toY,
        TimeSpan dur, System.Windows.Media.Animation.IEasingFunction ease)
    {
        if (tb.RenderTransform is not TranslateTransform tt)
            tb.RenderTransform = tt = new TranslateTransform();
        tb.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(fromOp, toOp, dur) { EasingFunction = ease });
        tt.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(fromY, toY, dur) { EasingFunction = ease });
        tb.Effect = new BlurEffect { Radius = 0 };
        ((BlurEffect)tb.Effect).BeginAnimation(BlurEffect.RadiusProperty,
            new System.Windows.Media.Animation.DoubleAnimation(toOp > fromOp ? 6 : 0, toOp > fromOp ? 0 : 6, dur));
    }

    /// <summary>Sung part white, the rest album-tinted, with a soft edge.</summary>
    private void UpdateSweep(double progress)
    {
        if (_satSweep is null) return;
        var edge = 0.06;
        var p = progress * (1 + edge * 2) - edge; // let the edge fully enter and leave
        var unsung = Color.FromArgb(0xB0, _satTint.R, _satTint.G, _satTint.B);
        _satSweep.GradientStops.Clear();
        _satSweep.GradientStops.Add(new GradientStop(Colors.White, Math.Clamp(p, 0, 1)));
        _satSweep.GradientStops.Add(new GradientStop(unsung, Math.Clamp(p + edge, 0, 1)));
    }

    /// <summary>Builds the capsule + neck for the current spring values.</summary>
    private void LayoutSatellite()
    {
        var H = _vm.ClosedHeight;
        var scale = _hoverScale.ScaleX;
        var centre = WindowRoot.ActualWidth / 2;
        var surfW = double.IsNaN(Surface.Width) ? Surface.ActualWidth : Surface.Width;
        var notchRight = centre + surfW / 2 * scale;
        var notchH = H * _hoverScale.ScaleY;
        var cy = notchH / 2;

        var (shape, capsuleRect) = BuildSatelliteGeometry(notchRight, notchH, H, _sep, _satW);
        _satPath!.Data = shape;
        var left = capsuleRect.Left;
        var top = capsuleRect.Top;
        var w = capsuleRect.Width;
        var h = capsuleRect.Height;
        var cx = capsuleRect.Left + w / 2;

        // Text / note inside the capsule, clipped to it.
        var textOpacity = SmoothStep(0.72, 1.0, Math.Clamp(_sep, 0, 1.2));
        _satTextHost!.Visibility = Visibility.Visible;
        _satTextHost.Opacity = textOpacity;
        _satTextHost.Clip = new RectangleGeometry(new Rect(left, top, w, h), h / 2, h / 2);
        foreach (var tb in new[] { _satTextA!, _satTextB! })
        {
            tb.Width = Math.Max(0, w - SatPadX * 2);
            Canvas.SetLeft(tb, left + SatPadX);
            Canvas.SetTop(tb, cy - SatFontSize * 0.68);
        }
        Canvas.SetLeft(_satNote!, cx - 5);
        Canvas.SetTop(_satNote!, cy - 6);
    }

    /// <summary>
    /// The capsule ∪ liquid neck for a separation <paramref name="sep"/> (0 =
    /// absorbed into the notch, 1 = resting <see cref="SatGap"/> away; may
    /// overshoot). While approaching, the capsule narrows toward a circle and
    /// sinks under the notch's trailing edge; within <see cref="SatNeckLength"/>
    /// of it a neck with pinched cubic sides joins the two, thinning to nothing
    /// as they part.
    /// </summary>
    public static (Geometry Shape, Rect Capsule) BuildSatelliteGeometry(
        double notchRight, double notchH, double closedH, double sep, double satW)
    {
        var H = closedH;
        var cy = notchH / 2;
        var s = Math.Clamp(sep, 0, 1);
        // A small drop buds off the notch edge, detaches (left edge moving
        // monotonically outward, so the neck forms and snaps exactly once), then
        // stretches rightward into the full capsule. Overshoot past 1 nudges it
        // a little further out — the spring's bounce.
        var h = Lerp(H * 0.62, H, SmoothStep(0.0, 0.5, s));
        var w = Lerp(h, satW, SmoothStep(0.3, 1.0, s));
        var gap = Lerp(-h * 0.85, SatGap, SmoothStep(0.0, 0.7, s)) + Math.Max(0, sep - 1) * 40;
        var rect = new Rect(notchRight + gap, cy - h / 2, w, h);

        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(new RectangleGeometry(rect, h / 2, h / 2));

        if (gap < SatNeckLength)
        {
            // Root the neck inside the notch's straight edge, clear of its rounded
            // bottom corner, so its sides leave the notch smoothly.
            var x0 = notchRight - 7;
            var x1 = rect.Left + h * 0.5;
            var a = Math.Min(notchH * 0.27, h * 0.42);     // half-height at the notch
            var a2 = h * 0.42;                              // half-height at the capsule
            var waist = Math.Min(a, a2) * Math.Pow(Math.Clamp(1 - gap / SatNeckLength, 0, 1), 1.35);
            // Cubic whose midpoint sits at the waist: mid = (a + a2)/8 + 0.75c.
            var c = (waist - 0.125 * (a + a2)) / 0.75;
            if (waist > 0.8 && x1 > x0)
            {
                var span = x1 - x0;
                var fig = new PathFigure { StartPoint = new Point(x0, cy - a), IsClosed = true };
                fig.Segments.Add(new BezierSegment(new Point(x0 + span * 0.5, cy - c), new Point(x1 - span * 0.5, cy - c), new Point(x1, cy - a2), true));
                fig.Segments.Add(new LineSegment(new Point(x1, cy + a2), true));
                fig.Segments.Add(new BezierSegment(new Point(x1 - span * 0.5, cy + c), new Point(x0 + span * 0.5, cy + c), new Point(x0, cy + a), true));
                group.Children.Add(new PathGeometry(new[] { fig }));
            }
        }
        return (group, rect);
    }

    /// <summary>Semi-implicit damped spring (SwiftUI response / dampingFraction).</summary>
    private static void Spring(ref double x, ref double v, double target, double response, double damping, double dt)
    {
        var k = Math.Pow(2 * Math.PI / response, 2);
        var c = 4 * Math.PI * damping / response;
        // Sub-step for stability on long frames.
        var steps = Math.Max(1, (int)Math.Ceiling(dt / (1 / 240.0)));
        var h = dt / steps;
        for (var i = 0; i < steps; i++)
        {
            v += (-k * (x - target) - c * v) * h;
            x += v * h;
        }
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double SmoothStep(double e0, double e1, double x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static Color Brighten(Color c, double minLuma)
    {
        var luma = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        if (luma >= minLuma) return c;
        var f = luma <= 0.01 ? 1 : minLuma / luma;
        byte B(double v) => (byte)Math.Clamp(v * f + (luma <= 0.01 ? 160 : 0), 0, 255);
        return Color.FromRgb(B(c.R), B(c.G), B(c.B));
    }
}
