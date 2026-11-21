using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Closed-notch camera / microphone in-use indicator — a port of the original's
/// <c>PrivacyLiveActivity</c>. The pill grows by a 40pt wing on the right that
/// holds the active indicator; when both are active the microphone moves to a
/// matching left wing and the camera stays right. Icons are the original's
/// <c>PrivacyIcon</c>: mic.fill in orange, video.fill in green, 13pt semibold.
/// </summary>
public sealed class ClosedPrivacyActivity : UserControl
{
    /// <summary>Wing width: effectiveClosedNotchHeight − 12 + 20.</summary>
    private const double Wing = 40;
    private const double EdgeInset = 8;

    private static readonly Brush CameraGreen = Frozen(Color.FromRgb(39, 205, 65));
    private static readonly Brush MicOrange = Frozen(Color.FromRgb(255, 149, 3));

    public bool Camera { get; }
    public bool Microphone { get; }

    public static double PillWidth(bool camera, bool microphone, double closedWidth) =>
        closedWidth + Wing + (camera && microphone ? Wing : 0);

    public ClosedPrivacyActivity(bool camera, bool microphone)
    {
        Camera = camera;
        Microphone = microphone;
        IsHitTestVisible = false;

        var both = camera && microphone;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(both ? Wing : 0) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Wing) });

        if (both)
        {
            var left = Icon(mic: true);
            left.HorizontalAlignment = HorizontalAlignment.Left;
            left.Margin = new Thickness(EdgeInset, 0, 0, 0);
            Grid.SetColumn(left, 0);
            root.Children.Add(left);
        }

        var right = Icon(mic: !camera);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        right.Margin = new Thickness(0, 0, EdgeInset, 0);
        Grid.SetColumn(right, 2);
        root.Children.Add(right);

        Content = root;
    }

    private static FrameworkElement Icon(bool mic)
    {
        var canvas = new Canvas { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
        if (mic)
        {
            // mic.fill: capsule head, cradle arc, stem and base.
            canvas.Children.Add(new Path
            {
                Data = new RectangleGeometry(new Rect(9.4, 4.6, 5.2, 9.2), 2.6, 2.6),
                Fill = MicOrange,
            });
            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse("M 7.4,11 A 4.6,4.6 0 0 0 16.6,11 M 12,15.8 V 18.4 M 9.6,18.6 H 14.4"),
                Stroke = MicOrange,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        else
        {
            // video.fill: rounded body + lens wedge.
            canvas.Children.Add(new Path
            {
                Data = new RectangleGeometry(new Rect(3.6, 7.4, 11.6, 9.2), 2.2, 2.2),
                Fill = CameraGreen,
            });
            canvas.Children.Add(new Path
            {
                Data = Geometry.Parse("M 16.2,10.8 L 20.6,8.3 V 15.7 L 16.2,13.2 Z"),
                Fill = CameraGreen,
                Stroke = CameraGreen,
                StrokeThickness = 0.8,
                StrokeLineJoin = PenLineJoin.Round,
            });
        }
        return canvas;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
