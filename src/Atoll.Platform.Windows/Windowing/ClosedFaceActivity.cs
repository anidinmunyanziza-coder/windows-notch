using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// The idle face on the empty closed pill — the original's built-in
/// "not human face" (DynamicIslandFaceAnimation with MinimalFaceFeatures):
/// a 20pt wing on the right holding two 4pt eyes over a 10×2 mouth, blinking
/// (eyes squash to 1pt) every 3 s. Shown when nothing is playing and
/// Defaults[.showNotHumanFace] is on.
/// </summary>
public sealed class ClosedFaceActivity : UserControl
{
    /// <summary>Wing = effectiveClosedNotchHeight − 12; HStack spacing 8 each side.</summary>
    private const double Wing = 20;
    private const double Spacing = 8;

    public static double PillWidth(double closedWidth) => closedWidth + 2 * (Wing + Spacing) - 20;

    private readonly ScaleTransform _leftEye = new(1, 1);
    private readonly ScaleTransform _rightEye = new(1, 1);
    private readonly DispatcherTimer _blink = new() { Interval = TimeSpan.FromSeconds(3) };

    public ClosedFaceActivity()
    {
        IsHitTestVisible = false;

        var eyes = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        eyes.Children.Add(Eye(_leftEye, new Thickness(0, 0, 4, 0)));
        eyes.Children.Add(Eye(_rightEye, new Thickness(0)));

        var mouth = new Border
        {
            Width = 10, Height = 2, CornerRadius = new CornerRadius(1),
            Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var face = new StackPanel
        {
            Width = Wing, Height = Wing,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { eyes, mouth },
        };
        // Centre the 10pt-tall face inside its 20pt wing.
        face.Margin = new Thickness(0, 0, 0, 0);
        face.Children[0].SetValue(MarginProperty, new Thickness(0, 5, 0, 0));

        var root = new Grid { Margin = new Thickness(0, 0, Spacing - 2, 0) };
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Wing) });
        Grid.SetColumn(face, 1);
        root.Children.Add(face);
        Content = root;

        _blink.Tick += (_, _) => Blink();
        Loaded += (_, _) => _blink.Start();
        Unloaded += (_, _) => _blink.Stop();
    }

    private static FrameworkElement Eye(ScaleTransform scale, Thickness margin) => new Border
    {
        Width = 4, Height = 4, CornerRadius = new CornerRadius(2),
        Background = Brushes.White, Margin = margin,
        RenderTransformOrigin = new Point(0.5, 0.5),
        RenderTransform = scale,
    };

    /// <summary>withAnimation(.spring(duration: 0.2)) { isBlinking = true } then
    /// back 0.1 s later — the eyes squash from 4pt to 1pt and reopen.</summary>
    private void Blink()
    {
        foreach (var eye in new[] { _leftEye, _rightEye })
        {
            var squash = new DoubleAnimationUsingKeyFrames();
            squash.KeyFrames.Add(new EasingDoubleKeyFrame(0.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100)),
                new CubicEase { EasingMode = EasingMode.EaseInOut }));
            squash.KeyFrames.Add(new EasingDoubleKeyFrame(0.25, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            squash.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(320)),
                new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }));
            eye.BeginAnimation(ScaleTransform.ScaleYProperty, squash);
        }
    }
}
