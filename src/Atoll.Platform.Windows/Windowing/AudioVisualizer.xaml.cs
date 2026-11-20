using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Atoll.Platform.Windows.Interop;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// A wide symmetric bar visualizer driven by the real output level
/// (<see cref="AudioMeter"/>). Each bar gets its own phase so the row ripples
/// like a spectrum even though the source is a single peak value (no FFT).
/// </summary>
public partial class AudioVisualizer : UserControl
{
    private const int BarCount = 26;
    private readonly Rectangle[] _bars = new Rectangle[BarCount];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private double _level;
    private double _t;

    public AudioVisualizer()
    {
        InitializeComponent();
        BuildBars();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        _timer.Tick += OnTick;
    }

    private void BuildBars()
    {
        var accent = Color.FromRgb(0x0A, 0x84, 0xFF);
        var top = Color.FromRgb(0x6F, 0xB8, 0xFF);
        for (int i = 0; i < BarCount; i++)
        {
            var bar = new Rectangle
            {
                Width = 3,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Height = 3,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Fill = new LinearGradientBrush(top, accent, 90),
            };
            _bars[i] = bar;
            var cell = new Grid();
            cell.Children.Add(bar);
            Bars.Children.Add(cell);
        }
    }

    private byte[]? _art;
    private bool _tinted;

    private void OnTick(object? sender, EventArgs e)
    {
        // Tint with the album gradient (re-tint only when the artwork changes).
        var media = (DataContext as Atoll.Core.ViewModels.ShellViewModel)?.MediaState;
        if (!_tinted || !ReferenceEquals(media?.ArtworkBytes, _art))
        {
            _art = media?.ArtworkBytes;
            _tinted = true;
            var brush = AlbumPalette.Gradient(_art);
            foreach (var bar in _bars) bar.Fill = brush;
        }

        var peak = media?.IsPlaying == false ? 0f : AudioMeter.Shared.Peak; // rest when paused
        var target = Math.Min(1.0, Math.Sqrt(peak) * 1.35);
        _level = _level * 0.6 + target * 0.4;
        _t += 0.033;

        var maxH = Math.Max(6.0, ActualHeight > 0 ? ActualHeight : 40);
        var center = (BarCount - 1) / 2.0;

        for (int i = 0; i < BarCount; i++)
        {
            var dist = Math.Abs(i - center) / center;       // 0 middle, 1 edges
            var bell = 1.0 - 0.45 * dist;                   // taller in the middle
            var wob = 0.55 + 0.45 * Math.Sin(_t * 7.0 + i * 0.55);
            var h = 3.0 + _level * (maxH - 3.0) * bell * wob;
            _bars[i].Height = Math.Clamp(h, 3.0, maxH);
        }
    }
}
