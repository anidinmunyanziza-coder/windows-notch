using System.Windows;
using System.Windows.Controls;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Compact media activity on the closed notch while a track plays — the
/// original's closed music live activity: rounded album art on the left, the
/// title in the centre, and the album-tinted spectrum on the right. The art and
/// spectrum are the matched-geometry endpoints the notch morphs into the open
/// player.
/// </summary>
public partial class ClosedMediaActivity : UserControl
{
    public ClosedMediaActivity()
    {
        InitializeComponent();
    }

    /// <summary>Container hidden while the art is "in flight" during a morph.</summary>
    public FrameworkElement ArtMorphHost => ArtHost;

    /// <summary>The visible art, measured as the morph endpoint.</summary>
    public FrameworkElement ArtMorphVisual => ArtVisual;

    public FrameworkElement SpectrumMorphHost => SpectrumHost;

    /// <summary>When the pill is turned 90° for a side-docked notch, keep the art
    /// and the waveform upright (the title reads down the edge like a spine).</summary>
    public void SetIconsUpright(double counterAngle)
    {
        foreach (var el in new FrameworkElement[] { ArtHost, SpectrumHost })
        {
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = counterAngle == 0 ? System.Windows.Media.Transform.Identity : new System.Windows.Media.RotateTransform(counterAngle);
        }
    }
}
