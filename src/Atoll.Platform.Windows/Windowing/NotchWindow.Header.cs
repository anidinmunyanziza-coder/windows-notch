using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Atoll.Core.Models;
using Atoll.Platform.Windows.Interop;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// The open notch's top strip — quick actions on the right (clipboard, screen
/// colour picker, settings) and the battery, styled after Apple's status bars:
/// thin-stroke SF-Symbol-style glyphs in secondary white (drawn, never font
/// icons) that turn white over a soft circular halo on hover, and an iOS
/// battery — hairline body, inset fill (green while charging, red at 20% or
/// less, white otherwise) with a bolt while charging, percentage beside it.
/// </summary>
public partial class NotchWindow
{
    private const double BatteryInnerWidth = 19.0;

    private static readonly Brush HeaderIconRest = FrozenBrush(Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BatteryOutline = FrozenBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    private static readonly Brush BatteryGreen = FrozenBrush(Color.FromRgb(0x30, 0xD1, 0x58));
    private static readonly Brush BatteryRed = FrozenBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly Brush BatteryWhite = FrozenBrush(Color.FromRgb(0xF5, 0xF5, 0xF7));

    private Border? _batteryFill;
    private Path? _batteryBolt;
    private TextBlock? _batteryText;
    private FrameworkElement? _batteryGroup;

    // ── SF-Symbol-style glyphs on a 24×24 grid ──────────────────────────────

    /// <summary>clipboard: board with a clip tab and two list lines.</summary>
    private static readonly Geometry ClipboardGlyph = Geometry.Parse(
        "M 8.4,4.6 H 7 C 5.6,4.6 4.6,5.6 4.6,7 V 18.6 C 4.6,20 5.6,21 7,21 H 17 C 18.4,21 19.4,20 19.4,18.6 V 7 " +
        "C 19.4,5.6 18.4,4.6 17,4.6 H 15.6 M 9.6,3 H 14.4 C 15.1,3 15.6,3.5 15.6,4.2 V 5.2 C 15.6,5.9 15.1,6.4 14.4,6.4 " +
        "H 9.6 C 8.9,6.4 8.4,5.9 8.4,5.2 V 4.2 C 8.4,3.5 8.9,3 9.6,3 Z M 8.6,11.4 H 15.4 M 8.6,15.4 H 13.4");

    /// <summary>eyedropper: bulb, collar and a slim tapering pipette.</summary>
    private static readonly Geometry EyedropperGlyph = Geometry.Parse(
        "M 13.6,7.4 L 16,5 C 17.1,3.9 18.9,3.9 20,5 C 21.1,6.1 21.1,7.9 20,9 L 17.6,11.4 " +
        "M 12.2,6 L 19,12.8 M 15.4,9.6 L 7.4,17.6 C 6.9,18.1 6.6,18.4 6,18.6 L 4.4,19.6 L 5.4,18 " +
        "C 5.6,17.4 5.9,17.1 6.4,16.6 L 14.4,8.6");

    /// <summary>gearshape: eight rounded teeth around a centre ring.</summary>
    private static readonly Geometry GearGlyph = BuildGear();

    private static Geometry BuildGear()
    {
        const int teeth = 8;
        const double cx = 12, cy = 12, outer = 9.2, inner = 7.1, hole = 2.9;
        var period = 2 * Math.PI / teeth;
        var fig = new PathFigure { IsClosed = true };
        for (var i = 0; i < teeth; i++)
        {
            var a = i * period - Math.PI / 2;
            Point P(double r, double ang) => new(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang));
            var pts = new[]
            {
                P(inner, a - 0.31 * period), P(outer, a - 0.17 * period),
                P(outer, a + 0.17 * period), P(inner, a + 0.31 * period),
            };
            if (i == 0) fig.StartPoint = pts[0];
            else fig.Segments.Add(new ArcSegment(pts[0], new Size(inner, inner), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(pts[1], true));
            fig.Segments.Add(new ArcSegment(pts[2], new Size(outer, outer), 0, false, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(pts[3], true));
        }
        // close back to the first tooth along the root circle
        fig.Segments.Add(new ArcSegment(fig.StartPoint, new Size(inner, inner), 0, false, SweepDirection.Clockwise, true));

        var group = new GeometryGroup();
        group.Children.Add(new PathGeometry(new[] { fig }));
        group.Children.Add(new EllipseGeometry(new Point(cx, cy), hole, hole));
        group.Freeze();
        return group;
    }

    private static readonly Geometry BoltGlyph = Geometry.Parse("M 6.6,0 L 1.4,6.4 H 4.6 L 3.6,11 L 8.8,4.6 H 5.6 Z");

    // ────────────────────────────────────────────────────────────────────────

    private Border BuildStatusHeader()
    {
        _batteryText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = FrozenBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0),
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(_batteryText, FontNumeralAlignment.Tabular);

        // iOS battery: hairline body, inset rounded fill, optional bolt, nub.
        _batteryFill = new Border
        {
            CornerRadius = new CornerRadius(1.8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = BatteryInnerWidth,
        };
        _batteryBolt = new Path
        {
            Data = BoltGlyph,
            Fill = Brushes.White,
            Stroke = Brushes.Black,
            StrokeThickness = 1.1,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 8.5, Height = 11, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        var body = new Border
        {
            Width = 25,
            Height = 12,
            CornerRadius = new CornerRadius(3.6),
            BorderBrush = BatteryOutline,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1.5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid { Children = { _batteryFill } },
        };
        var bodyWithBolt = new Grid { Children = { body, _batteryBolt } };
        var nub = new Border
        {
            Width = 1.6, Height = 4.2,
            CornerRadius = new CornerRadius(0, 1, 1, 0),
            Background = BatteryOutline,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1, 0, 0, 0),
        };
        var battery = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _batteryText, bodyWithBolt, nub },
        };
        _batteryGroup = battery;

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(HeaderIconButton(ClipboardGlyph, "Clipboard", (_, _) => SwitchToTab(NotchView.Clipboard)));
        panel.Children.Add(HeaderIconButton(EyedropperGlyph, "Pick a colour", (_, _) => PickScreenColor()));
        panel.Children.Add(HeaderIconButton(GearGlyph, "Settings", (_, _) => _shell!.RequestOpenSettings()));
        panel.Children.Add(new Border { Width = 10 }); // gap before battery
        panel.Children.Add(battery);

        return new Border
        {
            Height = 26,
            Padding = new Thickness(14, 6, 18, 0),
            Child = panel,
        };
    }

    private static Button HeaderIconButton(Geometry glyph, string tip, RoutedEventHandler onClick)
    {
        var icon = new Path
        {
            Data = glyph,
            Width = 15, Height = 15,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        icon.SetBinding(Shape.StrokeProperty, new Binding(nameof(Button.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1),
        });

        var button = new Button
        {
            Content = icon,
            Width = 26,
            Height = 22,
            Margin = new Thickness(1, 0, 1, 0),
            ToolTip = tip,
            Style = HeaderIconStyle(),
        };
        button.Click += onClick;
        return button;
    }

    private static Style? _headerIconStyle;

    /// <summary>The resting colour lives in a style (not a local value) so the
    /// template's hover trigger can override it.</summary>
    private static Style HeaderIconStyle()
    {
        if (_headerIconStyle is not null) return _headerIconStyle;
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, HeaderIconRest));
        style.Setters.Add(new Setter(Control.TemplateProperty, HeaderIconTemplate()));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Seal();
        _headerIconStyle = style;
        return style;
    }

    private static ControlTemplate? _headerIconTemplate;

    /// <summary>Borderless: a soft circular halo fades in and the glyph turns
    /// white on hover; pressing deepens the halo.</summary>
    private static ControlTemplate HeaderIconTemplate()
    {
        if (_headerIconTemplate is not null) return _headerIconTemplate;

        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

        var halo = new FrameworkElementFactory(typeof(Ellipse)) { Name = "Halo" };
        halo.SetValue(Shape.FillProperty, Brushes.White);
        halo.SetValue(UIElement.OpacityProperty, 0.0);
        halo.SetValue(FrameworkElement.WidthProperty, 22.0);
        halo.SetValue(FrameworkElement.HeightProperty, 22.0);
        root.AppendChild(halo);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        root.AppendChild(presenter);

        var ct = new ControlTemplate(typeof(Button)) { VisualTree = root };

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.12, "Halo"));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        ct.Triggers.Add(hover);

        var pressed = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.2, "Halo"));
        ct.Triggers.Add(pressed);

        ct.Seal();
        _headerIconTemplate = ct;
        return ct;
    }

    private void RefreshBattery()
    {
        if (_batteryFill is null || _batteryText is null || _statusHeader is null || _batteryGroup is null) return;

        if (!NativeMethods.GetSystemPowerStatus(out var s)
            || (s.BatteryFlag & 128) != 0 || s.BatteryLifePercent == 255)
        {
            // No reading, or a desktop with no battery: keep the quick actions.
            _batteryGroup.Visibility = Visibility.Collapsed;
            return;
        }

        _batteryGroup.Visibility = Visibility.Visible;
        var pct = Math.Clamp((int)s.BatteryLifePercent, 0, 100);
        var charging = s.ACLineStatus == 1 || (s.BatteryFlag & 8) != 0;

        _batteryFill.Width = Math.Max(2, BatteryInnerWidth * pct / 100.0);
        _batteryFill.Background = charging ? BatteryGreen : pct <= 20 ? BatteryRed : BatteryWhite;
        _batteryBolt!.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
        _batteryText.Text = pct + "%";
    }

    private static Brush FrozenBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
