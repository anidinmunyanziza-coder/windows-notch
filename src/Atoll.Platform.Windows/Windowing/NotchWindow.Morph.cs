using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Atoll.Core.Design;
using Atoll.Core.Models;
using Atoll.Platform.Windows.Animation;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// The notch's morphing, mirroring the original SwiftUI view:
/// <list type="bullet">
/// <item><b>Matched geometry</b> (<c>matchedGeometryEffect</c> ids "albumArt"
/// and "spectrum"): when the notch opens/closes between the closed music pill
/// and the open player, the album art and the spectrum don't cross-fade — a
/// "hero" copy flies from where the element was in the outgoing view to where
/// it sits in the incoming one, interpolating position, size, corner radius and
/// opacity on the same spring as the notch shape, then hands off to the real
/// element.</item>
/// <item><b>Blur-replace</b> (<c>.transition(.blurReplace)</c>): everything
/// else in the outgoing view is snapshotted into a ghost that blurs and fades
/// out, while the incoming view blurs in.</item>
/// </list>
/// Heroes and ghosts live in <c>MorphLayer</c>, above the content and clipped to
/// the notch shape, in Surface-local coordinates.
/// </summary>
public partial class NotchWindow
{
    private static readonly ArtworkConverter MorphArtConverter = new();

    private sealed class Hero
    {
        public required FrameworkElement Visual;
        public required Rect From;
        public required Func<Rect?> To;
        public Border? RadiusBorder;
        public double FromRadius;
        public double ToRadius;
        public double FromOpacity = 1;
        public Func<double>? ToOpacity;
        public FrameworkElement? HiddenTarget;
        public Rect? LastTo;
    }

    /// <summary>Geometry captured from the outgoing view before the swap.</summary>
    private sealed class MorphPlan
    {
        public Rect? ArtFrom;
        public double ArtFromRadius;
        public double ArtFromOpacity = 1;
        public Rect? SpectrumFrom;
    }

    private readonly List<Hero> _heroes = new();
    private EventHandler? _heroHandler;
    private Stopwatch? _heroClock;
    private double _heroResponse = 0.42;
    private double _heroDamping = 1.0;

    // ------------------------------------------------------------------
    // Measuring
    // ------------------------------------------------------------------

    /// <summary>An element's on-screen bounds in Surface-local coordinates
    /// (including its own and its ancestors' render transforms).</summary>
    private Rect? MeasureInSurface(FrameworkElement? el)
    {
        if (el is null || !el.IsLoaded || el.ActualWidth <= 0 || el.ActualHeight <= 0) return null;
        if (!Surface.IsAncestorOf(el)) return null;
        try
        {
            return el.TransformToAncestor(Surface)
                     .TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Phase 1 — before the content swap: capture + ghost the outgoing view
    // ------------------------------------------------------------------

    private MorphPlan PrepareMorph(bool opening)
    {
        FinishMorph(); // settle anything still in flight

        var plan = new MorphPlan();
        if (opening)
        {
            // Closed pill → open notch.
            var closedChild = ContentHost.Child as FrameworkElement;
            if (closedChild is ClosedMediaActivity closed && _shell?.CurrentView == NotchView.Home)
            {
                plan.ArtFrom = MeasureInSurface(closed.ArtMorphVisual);
                plan.ArtFromRadius = 4;
                plan.SpectrumFrom = MeasureInSurface(closed.SpectrumMorphHost);
                // The flying copies take over; keep the originals out of the ghost.
                closed.ArtMorphHost.Opacity = 0;
                closed.SpectrumMorphHost.Opacity = 0;
            }
            if (closedChild is not null)
                GhostOut(closedChild, TimeSpan.FromMilliseconds(200));
        }
        else
        {
            // Open notch → closed pill.
            if (_currentContent is HomeTabContent home
                && DetermineClosedActivity() == ClosedPillActivity.Media)
            {
                plan.ArtFrom = MeasureInSurface(home.ArtMorphVisual);
                plan.ArtFromRadius = HomeTabContent.ArtCornerRadius;
                plan.ArtFromOpacity = home.ArtMorphVisual.Opacity;
                plan.SpectrumFrom = MeasureInSurface(home.SpectrumMorphHost);
                home.ArtMorphHost.Opacity = 0;
                home.SpectrumMorphHost.Opacity = 0;
            }
            if (_outerGrid is not null && ReferenceEquals(ContentHost.Child, _outerGrid))
                GhostOut(_outerGrid, TimeSpan.FromMilliseconds(220));
        }
        return plan;
    }

    // ------------------------------------------------------------------
    // Phase 2 — after the content swap: fly heroes into the incoming view
    // ------------------------------------------------------------------

    private void LaunchMorph(MorphPlan plan, bool opening)
    {
        FrameworkElement? artVisual = null, artHost = null, spectrumHost = null;
        double toRadius = 4;

        if (opening && _currentContent is HomeTabContent home)
        {
            artVisual = home.ArtMorphVisual;
            artHost = home.ArtMorphHost;
            spectrumHost = home.SpectrumMorphHost;
            toRadius = HomeTabContent.ArtCornerRadius;
        }
        else if (!opening && ContentHost.Child is ClosedMediaActivity closed)
        {
            artVisual = closed.ArtMorphVisual;
            artHost = closed.ArtMorphHost;
            spectrumHost = closed.SpectrumMorphHost;
            toRadius = 4;
        }

        var heroes = new List<Hero>();

        if (plan.ArtFrom is { } artFrom && artVisual is not null && artHost is not null)
        {
            var visual = BuildArtHero(plan.ArtFromRadius, out var radiusBorder);
            var target = artVisual;
            heroes.Add(new Hero
            {
                Visual = visual,
                From = artFrom,
                To = () => MeasureInSurface(target),
                RadiusBorder = radiusBorder,
                FromRadius = plan.ArtFromRadius,
                ToRadius = toRadius,
                FromOpacity = plan.ArtFromOpacity,
                ToOpacity = () => target.Opacity, // 0.4 while paused, like the original
                HiddenTarget = artHost,
            });
            artHost.Opacity = 0;
        }

        if (plan.SpectrumFrom is { } specFrom && spectrumHost is not null)
        {
            var target = spectrumHost;
            heroes.Add(new Hero
            {
                Visual = BuildSpectrumHero(),
                From = specFrom,
                To = () => MeasureInSurface(target),
                HiddenTarget = spectrumHost,
            });
            spectrumHost.Opacity = 0;
        }

        if (heroes.Count == 0) return;
        StartMorph(heroes,
            opening ? DesignTokens.OpenSpringResponse : DesignTokens.CloseSpringResponse,
            opening ? DesignTokens.OpenSpringDamping : DesignTokens.CloseSpringDamping);
    }

    private FrameworkElement BuildArtHero(double radius, out Border radiusBorder)
    {
        var bytes = _shell?.MediaState?.ArtworkBytes;
        var image = MorphArtConverter.Convert(bytes, typeof(ImageSource), null, CultureInfo.InvariantCulture)
            as ImageSource;

        radiusBorder = new Border
        {
            CornerRadius = new CornerRadius(radius),
            Background = image is not null
                ? new ImageBrush(image) { Stretch = Stretch.UniformToFill }
                : new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E)),
            IsHitTestVisible = false,
        };
        return radiusBorder;
    }

    private FrameworkElement BuildSpectrumHero() => new Viewbox
    {
        Stretch = Stretch.Fill,
        IsHitTestVisible = false,
        Child = new SpectrumBars { BarCount = 4, BarHeight = 14, DataContext = _shell },
    };

    private void StartMorph(List<Hero> heroes, double response, double damping)
    {
        _heroes.AddRange(heroes);
        foreach (var h in heroes)
        {
            MorphLayer.Children.Add(h.Visual);
            Place(h, h.From, h.FromRadius, h.FromOpacity);
        }

        _heroResponse = Math.Max(response, 1e-3);
        _heroDamping = damping;
        _heroClock = Stopwatch.StartNew();
        _heroHandler = OnHeroFrame;
        CompositionTarget.Rendering += _heroHandler;
    }

    private void OnHeroFrame(object? sender, EventArgs e)
    {
        if (_heroClock is null) return;

        var t = Math.Clamp(_heroClock.Elapsed.TotalSeconds / _heroResponse, 0.0, 1.0);
        var p = SpringMath.Progress(t, _heroDamping);

        foreach (var h in _heroes)
        {
            // Targets are re-measured every frame: the incoming view is still
            // laying out and sliding in while the notch grows.
            var to = h.To() ?? h.LastTo ?? h.From;
            h.LastTo = to;

            var rect = new Rect(
                SpringMath.Lerp(h.From.X, to.X, p),
                SpringMath.Lerp(h.From.Y, to.Y, p),
                Math.Max(0.5, SpringMath.Lerp(h.From.Width, to.Width, p)),
                Math.Max(0.5, SpringMath.Lerp(h.From.Height, to.Height, p)));

            var toOpacity = h.ToOpacity?.Invoke() ?? 1.0;
            Place(h, rect,
                SpringMath.Lerp(h.FromRadius, h.ToRadius, p),
                SpringMath.Lerp(h.FromOpacity, toOpacity, p));
        }

        if (t >= 1.0) FinishMorph();
    }

    private static void Place(Hero h, Rect r, double radius, double opacity)
    {
        Canvas.SetLeft(h.Visual, r.X);
        Canvas.SetTop(h.Visual, r.Y);
        h.Visual.Width = r.Width;
        h.Visual.Height = r.Height;
        if (h.RadiusBorder is not null)
            h.RadiusBorder.CornerRadius = new CornerRadius(Math.Max(0, radius));
        h.Visual.Opacity = Math.Clamp(opacity, 0, 1);
    }

    /// <summary>Lands every hero: removes the flying copies and reveals the real
    /// elements they stood in for.</summary>
    private void FinishMorph()
    {
        if (_heroHandler is not null)
        {
            CompositionTarget.Rendering -= _heroHandler;
            _heroHandler = null;
        }
        _heroClock = null;

        foreach (var h in _heroes)
        {
            MorphLayer.Children.Remove(h.Visual);
            if (h.HiddenTarget is not null) h.HiddenTarget.Opacity = 1;
        }
        _heroes.Clear();
    }

    // ------------------------------------------------------------------
    // Blur-replace
    // ------------------------------------------------------------------

    /// <summary>Snapshots <paramref name="el"/> into a ghost in the morph layer
    /// that blurs and fades away, so the outgoing view dissolves instead of
    /// vanishing when the real content is swapped.</summary>
    private void GhostOut(FrameworkElement el, TimeSpan duration)
    {
        if (MeasureInSurface(el) is not { } r || r.Width < 1 || r.Height < 1) return;

        try
        {
            var sx = DipScaleX;
            var sy = DipScaleY;
            var rtb = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(r.Width * sx)),
                Math.Max(1, (int)Math.Ceiling(r.Height * sy)),
                96 * sx, 96 * sy, PixelFormats.Pbgra32);

            // Render through a VisualBrush so the element's own layout offset
            // doesn't shift the snapshot.
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var brush = new VisualBrush(el)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, el.ActualWidth, el.ActualHeight),
                };
                dc.DrawRectangle(brush, null, new Rect(0, 0, r.Width, r.Height));
            }
            rtb.Render(dv);
            rtb.Freeze();

            var blur = new BlurEffect { Radius = 0, KernelType = KernelType.Gaussian };
            var ghost = new Image
            {
                Source = rtb,
                Width = r.Width,
                Height = r.Height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
                Effect = blur,
            };
            Canvas.SetLeft(ghost, r.X);
            Canvas.SetTop(ghost, r.Y);
            MorphLayer.Children.Insert(0, ghost); // beneath any heroes

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = ease };
            fade.Completed += (_, _) => MorphLayer.Children.Remove(ghost);
            ghost.BeginAnimation(OpacityProperty, fade);
            blur.BeginAnimation(BlurEffect.RadiusProperty,
                new DoubleAnimation(0, 14, duration) { EasingFunction = ease });
        }
        catch
        {
            // A snapshot is cosmetic; never let it break the transition.
        }
    }

    /// <summary>Fades and un-blurs an element into view (blur-replace "in").</summary>
    private static void FadeBlurIn(UIElement el, TimeSpan duration)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        BlurIn(el, duration);
    }

    private static void BlurIn(UIElement el, TimeSpan duration)
    {
        var blur = new BlurEffect { Radius = 12, KernelType = KernelType.Gaussian };
        el.Effect = blur;
        var anim = new DoubleAnimation(12, 0, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        anim.Completed += (_, _) =>
        {
            // Drop the effect once settled so the content renders crisp.
            if (ReferenceEquals(el.Effect, blur)) el.Effect = null;
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, anim);
    }
}
