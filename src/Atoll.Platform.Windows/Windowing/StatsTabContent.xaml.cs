using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Atoll.Core.ViewModels;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// System stats tab — a row of live metric cards (CPU, Memory, GPU, …) each
/// with a rolling sparkline, mirroring the original NotchStatsView. Which cards
/// appear is controlled by the Settings → Stats Feature toggles
/// (showCpuGraph / showMemoryGraph / showGpuGraph / showNetworkGraph /
/// showDiskGraph). CPU and Memory carry real PDH data; metrics with no Windows
/// data source show an honest "no data" state rather than a fabricated line.
/// </summary>
public partial class StatsTabContent : UserControl
{
    private const int HistoryLength = 40;
    private const double SparkHeight = 34;

    private sealed class MetricCard
    {
        public required string SettingKey;
        public required string Label;
        public required Color Accent;
        public required Func<ShellViewModel, double?> Read;
        public Polyline Line = null!;
        public Polygon Fill = null!;
        public TextBlock Value = null!;
        public Grid SparkHost = null!;
        public readonly Queue<double> History = new();
        public bool EverHadData;
    }

    private readonly List<MetricCard> _cards = new();
    private ShellViewModel? _shell;

    public StatsTabContent()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _shell = DataContext as ShellViewModel;
        BuildCards();
        if (_shell is not null)
        {
            _shell.PropertyChanged += OnShellChanged;
            Seed(_shell);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_shell is not null)
            _shell.PropertyChanged -= OnShellChanged;
    }

    private void BuildCards()
    {
        CardsHost.Children.Clear();
        _cards.Clear();

        var defs = new[]
        {
            new MetricCard { SettingKey = "showCpuGraph",     Label = "CPU",     Accent = Color.FromRgb(0x0A,0x84,0xFF), Read = s => s.MetricsSnapshot?.CpuTotalPercent },
            new MetricCard { SettingKey = "showMemoryGraph",  Label = "Memory",  Accent = Color.FromRgb(0x30,0xD1,0x58), Read = s => s.MetricsSnapshot?.MemoryUsedPercent },
            new MetricCard { SettingKey = "showGpuGraph",     Label = "GPU",     Accent = Color.FromRgb(0xBF,0x5A,0xF2), Read = s => s.MetricsSnapshot?.GpuDevices?.FirstOrDefault()?.UtilizationPercent },
            new MetricCard { SettingKey = "showNetworkGraph", Label = "Network", Accent = Color.FromRgb(0xFF,0x9F,0x0A), Read = _ => null },
            new MetricCard { SettingKey = "showDiskGraph",    Label = "Disk",    Accent = Color.FromRgb(0xFF,0x37,0x5F), Read = _ => null },
        };

        // Defaults mirror the video: CPU/Memory/GPU on, Network/Disk off.
        bool On(string key, bool fallback) => _shell?.GetSetting(key, fallback) ?? fallback;

        foreach (var def in defs)
        {
            var defaultOn = def.SettingKey is "showCpuGraph" or "showMemoryGraph" or "showGpuGraph";
            if (!On(def.SettingKey, defaultOn)) continue;
            _cards.Add(def);

            var visual = CreateCardVisual(def);
            visual.Cursor = System.Windows.Input.Cursors.Hand;
            var captured = def;
            visual.MouseLeftButtonUp += (_, _) => ShowDetail(captured);
            CardsHost.Children.Add(visual);
        }

        EmptyNote.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardsHost.Columns = Math.Max(1, _cards.Count);
    }

    private static FrameworkElement CreateCardVisual(MetricCard card)
    {
        var accent = new SolidColorBrush(card.Accent);

        card.Value = new TextBlock
        {
            Text = "—",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 2, 0, 0),
        };

        card.Fill = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x2E, card.Accent.R, card.Accent.G, card.Accent.B)),
            IsHitTestVisible = false,
        };
        card.Line = new Polyline
        {
            Stroke = accent,
            StrokeThickness = 1.6,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        card.SparkHost = new Grid { Height = SparkHeight, Margin = new Thickness(0, 6, 0, 0), ClipToBounds = true };
        card.SparkHost.Children.Add(card.Fill);
        card.SparkHost.Children.Add(card.Line);
        card.SparkHost.SizeChanged += (_, _) => Redraw(card);

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 7, Height = 7, Fill = accent,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0),
        });
        header.Children.Add(new TextBlock
        {
            Text = card.Label, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9E)),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(card.Value);
        stack.Children.Add(card.SparkHost);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(11, 9, 11, 10),
            Margin = new Thickness(4, 0, 4, 0),
            Child = stack,
        };
    }

    private void Seed(ShellViewModel shell)
    {
        // Prime each card with the current reading so the sparkline isn't empty.
        PushSample(shell);
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.MetricsSnapshot) || _shell is not { } shell) return;
        // Metrics are published from the poll timer's thread.
        if (Dispatcher.CheckAccess())
            PushSample(shell);
        else
            Dispatcher.BeginInvoke(() => PushSample(shell));
    }

    private void PushSample(ShellViewModel shell)
    {
        foreach (var card in _cards)
        {
            var reading = card.Read(shell);
            if (reading is { } v)
            {
                card.EverHadData = true;
                card.History.Enqueue(Math.Clamp(v, 0, 100));
                while (card.History.Count > HistoryLength) card.History.Dequeue();
                card.Value.Text = $"{v:0}%";
            }
            else if (!card.EverHadData)
            {
                card.Value.Text = "—";
            }
            Redraw(card);
        }

        if (_detailCard is not null) UpdateDetail();
    }

    // ------------------------------------------------------------------
    // Detail view
    // ------------------------------------------------------------------

    private MetricCard? _detailCard;

    private void ShowDetail(MetricCard card)
    {
        _detailCard = card;
        DetailDot.Fill = new SolidColorBrush(card.Accent);
        DetailName.Text = card.Label;
        DetailLine.Stroke = new SolidColorBrush(card.Accent);
        DetailFill.Fill = new SolidColorBrush(Color.FromArgb(0x2E, card.Accent.R, card.Accent.G, card.Accent.B));
        DetailSparkHost.SizeChanged -= OnDetailSparkSizeChanged;
        DetailSparkHost.SizeChanged += OnDetailSparkSizeChanged;

        CardsHost.Visibility = Visibility.Collapsed;
        DetailView.Visibility = Visibility.Visible;
        UpdateDetail();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        _detailCard = null;
        DetailView.Visibility = Visibility.Collapsed;
        CardsHost.Visibility = Visibility.Visible;
    }

    private void OnDetailSparkSizeChanged(object sender, SizeChangedEventArgs e) => RedrawDetailSpark();

    private void UpdateDetail()
    {
        if (_detailCard is null || _shell is null) return;
        var reading = _detailCard.Read(_shell);
        DetailValue.Text = reading is { } v ? $"{v:0}%" : "—";
        DetailInfo.Text = DetailInfoFor(_detailCard, _shell);
        RedrawDetailSpark();
    }

    private static string DetailInfoFor(MetricCard card, ShellViewModel shell)
    {
        var snap = shell.MetricsSnapshot;
        return card.SettingKey switch
        {
            "showCpuGraph" => $"Logical processors: {Environment.ProcessorCount}",
            "showMemoryGraph" => snap?.MemoryUsedBytes is { } used && snap.MemoryTotalBytes is { } total && total > 0
                ? $"Using {Gb(used):0.0} GB of {Gb(total):0.0} GB"
                : "Memory data unavailable",
            "showGpuGraph" => "No GPU data source on Windows (needs WMI / NVML / ADL).",
            "showNetworkGraph" => "No network counter available in this build.",
            "showDiskGraph" => "No disk counter available in this build.",
            _ => "",
        };
    }

    private static double Gb(ulong bytes) => bytes / (1024.0 * 1024.0 * 1024.0);

    private void RedrawDetailSpark()
    {
        if (_detailCard is null) return;
        var w = DetailSparkHost.ActualWidth;
        var h = DetailSparkHost.ActualHeight;
        if (w <= 0 || h <= 0 || _detailCard.History.Count == 0)
        {
            DetailLine.Points.Clear();
            DetailFill.Points.Clear();
            return;
        }

        var values = _detailCard.History.ToArray();
        var n = values.Length;
        var line = new PointCollection(n);
        var step = n > 1 ? w / (n - 1) : w;
        for (var i = 0; i < n; i++)
            line.Add(new Point(i * step, h - values[i] / 100.0 * h));
        DetailLine.Points = line;

        var fill = new PointCollection(line);
        fill.Insert(0, new Point(line[0].X, h));
        fill.Add(new Point(line[^1].X, h));
        DetailFill.Points = fill;
    }

    private static void Redraw(MetricCard card)
    {
        var w = card.SparkHost.ActualWidth;
        var h = card.SparkHost.ActualHeight > 0 ? card.SparkHost.ActualHeight : SparkHeight;
        if (w <= 0 || card.History.Count == 0) { card.Line.Points.Clear(); card.Fill.Points.Clear(); return; }

        var values = card.History.ToArray();
        var n = values.Length;
        var line = new PointCollection(n);
        var step = n > 1 ? w / (n - 1) : w;
        for (var i = 0; i < n; i++)
        {
            var x = i * step;
            var y = h - (values[i] / 100.0) * h;
            line.Add(new Point(x, y));
        }
        card.Line.Points = line;

        // Area fill: the line, closed down to the baseline.
        var fill = new PointCollection(line) { };
        fill.Insert(0, new Point(line[0].X, h));
        fill.Add(new Point(line[^1].X, h));
        card.Fill.Points = fill;
    }
}
