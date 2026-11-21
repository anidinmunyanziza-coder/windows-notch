using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Atoll.Core.Models;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>Settings that shape the inline HUD (Defaults keys of the same names).</summary>
public sealed record HudOptions(
    bool ShowProgressPercentages = true,
    bool UseColorCodedVolumeDisplay = true,
    bool UseColorCodedBatteryDisplay = true,
    bool ShowCapsLockLabel = false);

/// <summary>
/// The closed-notch inline HUD — a port of the original's <c>InlineHUD</c>.
/// Layout, left to right: [info] [gap: closed width − 20] [trailing], where
/// the info/trailing widths depend on the type (non-minimalistic values):
///   volume / brightness  92 | 92   icon + label | capsule bar + %
///   caps lock            56 | 0    icon only (88 trailing with "Caps Lock")
///   bluetooth audio      64 | 76   headphones   | circular battery ring
///                                  (80 and empty when no battery level)
/// The bar is the <c>DraggableProgressBar</c> look in inline mode (5pt capsule,
/// tertiary track); volume uses the colour-coded palette (green → yellow →
/// red), brightness a plain white ("hierarchical") fill.
/// </summary>
public sealed class ClosedHudActivity : UserControl
{
    private const double SideMargin = 10;
    private const double BarHeight = 5;

    private static (double Info, double Trailing) Widths(SneakContentType type, double value, HudOptions o) =>
        type switch
        {
            SneakContentType.CapsLock => (56, o.ShowCapsLockLabel ? 88 : 0),
            SneakContentType.BluetoothAudio => (64, value > 0 ? 76 : 80),
            _ => (92, 92),
        };

    /// <summary>Pill width for this HUD, given the idle closed-notch width.</summary>
    public static double PillWidth(SneakContentType type, double value, HudOptions o, double closedWidth)
    {
        var (info, trailing) = Widths(type, value, o);
        return SideMargin + info + Math.Max(closedWidth - 20, 0) + trailing + SideMargin;
    }

    public SneakContentType Type { get; }

    private readonly bool _colorCoded;
    private readonly Canvas _icon = new() { Width = 20, Height = 15, VerticalAlignment = VerticalAlignment.Center };

    // Volume / brightness bar
    private Border? _track;
    private Border? _fill;
    private readonly LinearGradientBrush _fillBrush = new() { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
    private TextBlock? _percent;
    private FrameworkElement? _barRow;
    private TextBlock? _mutedText;

    // Volume glyph parts (speaker.wave.N.fill / speaker.slash.fill).
    private readonly Path[] _waves = new Path[3];
    private Canvas? _slashGroup;
    // Brightness glyph parts (sun.min.fill / sun.max.fill).
    private Path? _sunRaysLong, _sunRaysShort;

    private double _value;
    private bool _muted;

    public ClosedHudActivity(SneakContentType type, double value, bool muted, HudOptions options)
    {
        Type = type;
        _colorCoded = type == SneakContentType.Volume && options.UseColorCodedVolumeDisplay;
        IsHitTestVisible = false;

        var (infoWidth, trailingWidth) = Widths(type, value, options);
        BuildIcon();

        var info = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _icon },
        };
        if (type is SneakContentType.Volume or SneakContentType.Brightness)
        {
            info.Children.Add(new TextBlock
            {
                Text = type == SneakContentType.Brightness ? "Brightness" : "Volume",
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 0, 0, 0),
            });
        }

        var trailing = new Grid
        {
            Margin = new Thickness(0, 0, trailingWidth > 0 ? 4 : 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        switch (type)
        {
            case SneakContentType.CapsLock:
                if (options.ShowCapsLockLabel)
                    trailing.Children.Add(new TextBlock
                    {
                        Text = "Caps Lock",
                        FontSize = 12.5,
                        FontWeight = FontWeights.Medium,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Right,
                    });
                break;

            case SneakContentType.BluetoothAudio:
                if (value > 0)
                    trailing.Children.Add(BatteryRing(value, options.UseColorCodedBatteryDisplay));
                break;

            default:
                BuildBar(trailing, options.ShowProgressPercentages);
                break;
        }

        var root = new Grid { Margin = new Thickness(SideMargin, 0, SideMargin, 0) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(infoWidth) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(trailingWidth) });
        Grid.SetColumn(info, 0);
        Grid.SetColumn(trailing, 2);
        root.Children.Add(info);
        root.Children.Add(trailing);
        Content = root;

        Update(value, muted, animate: false);
    }

    /// <summary>Applies a new level (0..1) and mute state, animating the change.</summary>
    public void Update(double value, bool muted) => Update(value, muted, animate: true);

    private void Update(double value, bool muted, bool animate)
    {
        _value = Math.Clamp(value, 0, 1);
        _muted = muted;
        if (_barRow is null) return; // caps lock / bluetooth are static

        // Volume shows "muted" at zero or when muted, like the original.
        var showMuted = Type == SneakContentType.Volume && (_muted || _value <= 0.0005);
        Fade(_barRow, showMuted ? 0 : 1, animate);
        Fade(_mutedText!, showMuted ? 1 : 0, animate);
        _barRow.RenderTransformOrigin = new Point(1, 0.5);
        _barRow.RenderTransform = new ScaleTransform(showMuted ? 0.9 : 1, showMuted ? 0.9 : 1);

        _percent!.Text = ((int)Math.Round(_value * 100)).ToString();
        if (_colorCoded) UpdatePalette();
        SetFillWidth(animate);
        UpdateIcon(animate, showMuted);
    }

    // ------------------------------------------------------------------
    // Volume / brightness bar
    // ------------------------------------------------------------------

    private void BuildBar(Grid host, bool showPercentage)
    {
        _fill = new Border
        {
            CornerRadius = new CornerRadius(BarHeight / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
            Background = _colorCoded ? _fillBrush : Brushes.White,
        };
        _track = new Border
        {
            Height = BarHeight,
            CornerRadius = new CornerRadius(BarHeight / 2),
            Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _fill,
        };
        _track.SizeChanged += (_, _) => SetFillWidth(animate: false);

        _percent = new TextBlock
        {
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            TextAlignment = TextAlignment.Right,
            Width = 28,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = showPercentage ? Visibility.Visible : Visibility.Collapsed,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        DockPanel.SetDock(_percent, Dock.Right);
        _barRow = new DockPanel { LastChildFill = true, Children = { _percent, _track } };

        _mutedText = new TextBlock
        {
            Text = "muted",
            FontSize = 11,
            FontWeight = FontWeights.Medium,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };

        host.Children.Add(_barRow);
        host.Children.Add(_mutedText);
    }

    private void SetFillWidth(bool animate)
    {
        if (_track is null || _fill is null) return;
        var target = Math.Max(0, _track.ActualWidth * _value);
        _fill.Opacity = _value <= 0 ? 0 : 1;
        if (!animate || _track.ActualWidth <= 0)
        {
            _fill.BeginAnimation(WidthProperty, null);
            _fill.Width = target;
            return;
        }
        // SwiftUI .smooth(duration: 0.18)
        _fill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    /// <summary>CircularBatteryIndicator: 16pt ring, 2pt track at 18% white,
    /// 2.2pt round-capped progress from 12 o'clock.</summary>
    private static UIElement BatteryRing(double value, bool colorCoded)
    {
        const double size = 16, r = 7;
        var v = Math.Clamp(value, 0.015, 1);
        var grid = new Grid
        {
            Width = size, Height = size,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Stroke = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            StrokeThickness = 2,
        });

        var angle = v * 2 * Math.PI;
        var start = new Point(size / 2, size / 2 - r);
        var end = new Point(size / 2 + r * Math.Sin(angle), size / 2 - r * Math.Cos(angle));
        Geometry arc;
        if (v >= 0.999)
            arc = new EllipseGeometry(new Point(size / 2, size / 2), r, r);
        else
        {
            var fig = new PathFigure { StartPoint = start };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true));
            arc = new PathGeometry(new[] { fig });
        }
        grid.Children.Add(new Path
        {
            Data = arc,
            Stroke = colorCoded ? new SolidColorBrush(SmoothColor(1 - v)) : Brushes.White,
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });
        return grid;
    }

    // ------------------------------------------------------------------
    // Colour-coded palette (ColorCodedPalette, smooth mode)
    // ------------------------------------------------------------------

    private static readonly (double R, double G, double B) Green = (0.2, 0.78, 0.35);
    private static readonly (double R, double G, double B) Yellow = (0.93, 0.75, 0.2);
    private static readonly (double R, double G, double B) Red = (0.95, 0.36, 0.3);

    private void UpdatePalette()
    {
        _fillBrush.GradientStops.Clear();
        _fillBrush.GradientStops.Add(new GradientStop(SmoothColor(Math.Max(_value - 0.15, 0)), 0));
        _fillBrush.GradientStops.Add(new GradientStop(SmoothColor(_value), 1));
    }

    /// <summary>Palette colour for an intensity in [0,1] (volume: level;
    /// battery: 1 − level).</summary>
    internal static Color SmoothColor(double intensity)
    {
        var t = Math.Clamp(intensity, 0, 1);
        if (t < 0.6) return ToColor(Green);
        if (t < 0.85) return Blend(Green, Yellow, (t - 0.6) / 0.25);
        return Blend(Yellow, Red, (t - 0.85) / 0.15);
    }

    private static Color Blend((double R, double G, double B) a, (double R, double G, double B) b, double f)
    {
        f = Math.Clamp(f, 0, 1);
        return ToColor((a.R + (b.R - a.R) * f, a.G + (b.G - a.G) * f, a.B + (b.B - a.B) * f));
    }

    private static Color ToColor((double R, double G, double B) c) =>
        Color.FromRgb((byte)Math.Round(c.R * 255), (byte)Math.Round(c.G * 255), (byte)Math.Round(c.B * 255));

    // ------------------------------------------------------------------
    // Icons — drawn, SF-Symbol-like
    // ------------------------------------------------------------------

    private void BuildIcon()
    {
        switch (Type)
        {
            case SneakContentType.Brightness:
            {
                // sun: filled disc + 8 rays; long rays = sun.max, short = sun.min.
                const double cx = 10, cy = 7.5;
                _icon.Children.Add(new Path
                {
                    Data = new EllipseGeometry(new Point(cx, cy), 3.1, 3.1),
                    Fill = Brushes.White,
                });
                _sunRaysLong = Rays(cx, cy, 4.9, 7.1, 1.5);
                _sunRaysShort = Rays(cx, cy, 5.0, 5.6, 1.6);
                _icon.Children.Add(_sunRaysLong);
                _icon.Children.Add(_sunRaysShort);
                return;
            }

            case SneakContentType.CapsLock:
                // capslock.fill: solid up-arrow over a short bar.
                _icon.Children.Add(new Path
                {
                    Data = Geometry.Parse("M 10,0.6 L 16.6,7.3 H 13.1 V 10.3 H 6.9 V 7.3 H 3.4 Z"),
                    Fill = Brushes.White,
                    Stroke = Brushes.White,
                    StrokeThickness = 0.9,
                    StrokeLineJoin = PenLineJoin.Round,
                });
                _icon.Children.Add(new Path
                {
                    Data = new RectangleGeometry(new Rect(6.9, 12.2, 6.2, 2.3), 0.8, 0.8),
                    Fill = Brushes.White,
                });
                return;

            case SneakContentType.BluetoothAudio:
                // headphones.fill: headband arc + two filled ear cups.
                _icon.Children.Add(new Path
                {
                    Data = Geometry.Parse("M 3.2,10 V 8.2 A 6.8,6.8 0 0 1 16.8,8.2 V 10"),
                    Stroke = Brushes.White,
                    StrokeThickness = 1.7,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                });
                _icon.Children.Add(new Path
                {
                    Data = new RectangleGeometry(new Rect(1.6, 8.6, 4.2, 6.2), 1.6, 1.6),
                    Fill = Brushes.White,
                });
                _icon.Children.Add(new Path
                {
                    Data = new RectangleGeometry(new Rect(14.2, 8.6, 4.2, 6.2), 1.6, 1.6),
                    Fill = Brushes.White,
                });
                return;
        }

        // Volume: speaker body
        _icon.Children.Add(new Path
        {
            Data = Geometry.Parse("M 0.5,5.1 H 3.1 L 7,1.6 V 13.4 L 3.1,9.9 H 0.5 Z"),
            Fill = Brushes.White,
            StrokeLineJoin = PenLineJoin.Round,
            Stroke = Brushes.White,
            StrokeThickness = 0.8,
        });
        // three arcs centred on (7, 7.5)
        double[] radii = { 3.0, 5.6, 8.2 };
        for (var i = 0; i < 3; i++)
        {
            var r = radii[i];
            var dx = r * Math.Cos(50 * Math.PI / 180);
            var dy = r * Math.Sin(50 * Math.PI / 180);
            var fig = new PathFigure { StartPoint = new Point(7 + dx, 7.5 - dy) };
            fig.Segments.Add(new ArcSegment(new Point(7 + dx, 7.5 + dy), new Size(r, r), 0, false,
                SweepDirection.Clockwise, true));
            _waves[i] = new Path
            {
                Data = new PathGeometry(new[] { fig }),
                Stroke = Brushes.White,
                StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            _icon.Children.Add(_waves[i]);
        }
        // slash for the muted / zero state, with a black cut-out behind it
        var slashData = Geometry.Parse("M 0.8,0.8 L 13.6,14.2");
        _slashGroup = new Canvas
        {
            Children =
            {
                new Path
                {
                    Data = slashData, Stroke = Brushes.Black, StrokeThickness = 3.6,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                },
                new Path
                {
                    Data = slashData, Stroke = Brushes.White, StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                },
            },
        };
        _icon.Children.Add(_slashGroup);
    }

    private static Path Rays(double cx, double cy, double r0, double r1, double thickness)
    {
        var geo = new GeometryGroup();
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            geo.Children.Add(new LineGeometry(
                new Point(cx + r0 * Math.Cos(a), cy + r0 * Math.Sin(a)),
                new Point(cx + r1 * Math.Cos(a), cy + r1 * Math.Sin(a))));
        }
        return new Path
        {
            Data = geo,
            Stroke = Brushes.White,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
    }

    private void UpdateIcon(bool animate, bool showMuted)
    {
        if (Type == SneakContentType.Brightness)
        {
            var max = _value > 0.6; // BrightnessSymbol: sun.max above 0.6
            Fade(_sunRaysLong!, max ? 1 : 0, animate);
            Fade(_sunRaysShort!, max ? 0 : 1, animate);
            return;
        }

        // SpeakerSymbol: 0 → speaker (slash), ≤0.3 → 1 wave, ≤0.8 → 2, else 3.
        var waves = showMuted ? 0 : _value <= 0.3 ? 1 : _value <= 0.8 ? 2 : 3;
        for (var i = 0; i < 3; i++)
            Fade(_waves[i], i < waves ? 1 : 0, animate);
        Fade(_slashGroup!, showMuted ? 1 : 0, animate);
    }

    private static void Fade(UIElement element, double to, bool animate)
    {
        if (!animate)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = to;
            return;
        }
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }
}
