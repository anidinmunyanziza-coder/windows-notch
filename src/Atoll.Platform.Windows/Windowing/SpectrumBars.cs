using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Atoll.Core.ViewModels;
using Atoll.Platform.Windows.Interop;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// The original's music spectrum (<c>MusicVisualizer</c> / <c>AudioSpectrum</c>):
/// <see cref="BarCount"/> capsule bars, 2px wide with 2px gaps, scaling on Y
/// about their vertical centre between 0.35 and 1.0 every 0.3 s, resting at
/// 0.35 when paused, filled with the album-colour gradient. The pulse amplitude
/// also follows the real output level so it reacts to the audio.
/// </summary>
public sealed class SpectrumBars : UserControl
{
    public int BarCount { get; set; } = 4;
    public double BarWidth { get; set; } = 2.0;
    public double BarHeight { get; set; } = 14.0;

    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private ScaleTransform[] _scales = Array.Empty<ScaleTransform>();
    private double[] _targets = Array.Empty<double>();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Random _rng = new();
    private byte[]? _art;
    private int _ticks;

    public SpectrumBars()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (_bars.Length == 0) Build();
            _timer.Start();
        };
        Unloaded += (_, _) => _timer.Stop();
        _timer.Tick += OnTick;
    }

    private void Build()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _bars = new Rectangle[BarCount];
        _scales = new ScaleTransform[BarCount];
        _targets = new double[BarCount];

        for (int i = 0; i < BarCount; i++)
        {
            var scale = new ScaleTransform(1, 0.35);
            var bar = new Rectangle
            {
                Width = BarWidth,
                Height = BarHeight,
                RadiusX = BarWidth / 2,
                RadiusY = BarWidth / 2,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale,
                Margin = new Thickness(i == 0 ? 0 : BarWidth, 0, 0, 0), // spacing = barWidth
            };
            _bars[i] = bar;
            _scales[i] = scale;
            _targets[i] = 0.35;
            panel.Children.Add(bar);
        }

        Content = panel;
        ApplyFill();
    }

    private void ApplyFill()
    {
        var brush = AlbumPalette.Gradient(_art);
        foreach (var b in _bars) b.Fill = brush;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var media = (DataContext as ShellViewModel)?.MediaState;

        // Re-tint when the artwork changes (same array reference = same track art).
        if (!ReferenceEquals(media?.ArtworkBytes, _art))
        {
            _art = media?.ArtworkBytes;
            ApplyFill();
        }

        var playing = media?.IsPlaying == true;

        // New random targets every ~0.3 s, like the original's timer.
        if (_ticks++ % 9 == 0)
        {
            var activity = playing
                ? Math.Clamp(0.45 + Math.Sqrt(AudioMeter.Shared.Peak) * 1.2, 0.0, 1.0)
                : 0.0;
            for (int i = 0; i < _targets.Length; i++)
                _targets[i] = 0.35 + _rng.NextDouble() * 0.65 * activity;
        }

        // Ease each bar toward its target.
        for (int i = 0; i < _scales.Length; i++)
        {
            var cur = _scales[i].ScaleY;
            _scales[i].ScaleY = cur + (_targets[i] - cur) * 0.28;
        }
    }
}
