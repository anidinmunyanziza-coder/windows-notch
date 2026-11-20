using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Atoll.Core.Contracts;
using Atoll.Core.ViewModels;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Compact timer live-activity shown on the closed notch while a countdown runs
/// — a depleting ring plus the remaining time (the original timer live
/// activity). Driven by the shared timer service.
/// </summary>
public partial class ClosedTimerActivity : UserControl
{
    private ShellViewModel? Shell => DataContext as ShellViewModel;
    private ITimerService? Timer => Shell?.TimerService;

    public ClosedTimerActivity()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Timer is { } t)
            {
                t.StateChanged += OnChanged;
                t.Tick += OnTick;
                Update(t.State);
            }
        };
        Unloaded += (_, _) =>
        {
            if (Timer is { } t)
            {
                t.StateChanged -= OnChanged;
                t.Tick -= OnTick;
            }
        };
    }

    private void OnChanged(object? sender, TimerState s) => Dispatcher.Invoke(() => Update(s));
    private void OnTick(object? sender, TimeSpan remaining) => Dispatcher.Invoke(() => Update(Timer?.State));

    private void Update(TimerState? state)
    {
        if (state is null) return;
        TimeText.Text = Format(state.Remaining);
        NameText.Text = state.Name ?? "";

        var fraction = state.Duration.TotalSeconds > 0
            ? Math.Clamp(state.Remaining.TotalSeconds / state.Duration.TotalSeconds, 0, 1)
            : 0;
        Ring.Data = BuildArc(fraction);
    }

    private static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static Geometry BuildArc(double fraction)
    {
        const double cx = 9, cy = 9, r = 7.75;
        var sweep = Math.Clamp(fraction, 0, 1) * 360.0;
        if (sweep <= 0.01) return Geometry.Empty;
        if (sweep >= 359.99) sweep = 359.99;

        var start = (-90.0) * Math.PI / 180.0;
        var end = (-90.0 + sweep) * Math.PI / 180.0;
        var p1 = new Point(cx + r * Math.Cos(start), cy + r * Math.Sin(start));
        var p2 = new Point(cx + r * Math.Cos(end), cy + r * Math.Sin(end));

        var fig = new System.Windows.Media.PathFigure { StartPoint = p1, IsClosed = false };
        fig.Segments.Add(new System.Windows.Media.ArcSegment
        {
            Point = p2,
            Size = new Size(r, r),
            IsLargeArc = sweep > 180,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geo = new System.Windows.Media.PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }
}
