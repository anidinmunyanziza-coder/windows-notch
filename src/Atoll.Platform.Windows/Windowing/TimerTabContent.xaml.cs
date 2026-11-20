using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Atoll.Core.Contracts;
using Atoll.Core.ViewModels;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Timer tab — preset chips when idle, an iOS-style circular countdown ring with
/// pause/reset while running. Mirrors the original NotchTimerView / TimerManager.
/// DataContext is the ShellViewModel (TimerState + ITimerService).
/// </summary>
public partial class TimerTabContent : UserControl
{
    private static readonly (string Label, int Minutes)[] Presets =
    {
        ("1 min", 1), ("5 min", 5), ("10 min", 10),
        ("15 min", 15), ("25 min", 25), ("60 min", 60),
    };

    private ShellViewModel? Shell => DataContext as ShellViewModel;
    private ITimerService? Timer => Shell?.TimerService;

    public TimerTabContent()
    {
        InitializeComponent();
        BuildPresets();

        Loaded += (_, _) =>
        {
            if (Timer is { } t)
            {
                t.StateChanged += OnTimerChanged;
                t.Tick += OnTick;
                UpdateUI(t.State);
            }
        };
        Unloaded += (_, _) =>
        {
            if (Timer is { } t)
            {
                t.StateChanged -= OnTimerChanged;
                t.Tick -= OnTick;
            }
        };
    }

    private void BuildPresets()
    {
        PresetPanel.Children.Clear();
        foreach (var (label, minutes) in Presets)
        {
            var chip = new Button
            {
                Content = label,
                Style = (Style)Resources["Chip"],
                Margin = new Thickness(4),
            };
            var m = minutes;
            chip.Click += async (_, _) =>
            {
                if (Timer is { } t) await t.StartAsync(TimeSpan.FromMinutes(m), label);
            };
            PresetPanel.Children.Add(chip);
        }
    }

    private void OnTimerChanged(object? sender, TimerState state) =>
        Dispatcher.Invoke(() => UpdateUI(state));

    private void OnTick(object? sender, TimeSpan remaining) =>
        Dispatcher.Invoke(() => UpdateUI(Timer?.State));

    private void UpdateUI(TimerState? state)
    {
        var active = state?.IsActive == true;
        IdlePanel.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        RunPanel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (!active || state is null) return;

        TimeText.Text = Format(state.Remaining);
        NameText.Text = state.Name ?? "";
        PauseButton.Content = state.IsPaused ? "" : ""; // Play when paused, else Pause

        var fraction = state.Duration.TotalSeconds > 0
            ? Math.Clamp(state.Remaining.TotalSeconds / state.Duration.TotalSeconds, 0, 1)
            : 0;
        ProgressArc.Data = BuildArc(fraction);
    }

    private static string Format(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    /// <summary>Arc filling <paramref name="fraction"/> of a circle, clockwise from
    /// the top — the remaining-time ring.</summary>
    private static Geometry BuildArc(double fraction)
    {
        const double cx = 59, cy = 59, r = 52.5;
        var sweep = Math.Clamp(fraction, 0, 1) * 360.0;
        if (sweep <= 0.01) return Geometry.Empty;
        if (sweep >= 359.99) sweep = 359.99;

        var startAngle = -90.0 * Math.PI / 180.0;
        var endAngle = (-90.0 + sweep) * Math.PI / 180.0;
        var start = new Point(cx + r * Math.Cos(startAngle), cy + r * Math.Sin(startAngle));
        var end = new Point(cx + r * Math.Cos(endAngle), cy + r * Math.Sin(endAngle));

        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(r, r),
            IsLargeArc = sweep > 180,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private async void OnPause(object sender, RoutedEventArgs e)
    {
        if (Timer is not { } t) return;
        if (t.State.IsPaused) await t.ResumeAsync();
        else await t.PauseAsync();
    }

    private async void OnReset(object sender, RoutedEventArgs e)
    {
        if (Timer is { } t) await t.StopAsync();
    }
}
