using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Atoll.Core.Usage;
using Atoll.Core.ViewModels;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// LLM usage tab — a port of the original's NotchLLMUsageView: one card per
/// enabled provider (Claude Code, Codex). With rate-limit data a card shows
/// Session (5 h) and Week gauges with reset times plus compact token totals;
/// without it, Today / Week / Session token counts with an API-equivalent cost
/// estimate and a "quota unavailable" note. Refreshes on open (at most once a
/// minute, like the original).
/// </summary>
public sealed class LlmUsageTabContent : UserControl
{
    private static readonly Brush Secondary = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Tertiary = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    private static readonly Brush CardBg = Frozen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Track = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Blue = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF));
    private static readonly Brush Orange = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A));
    private static readonly Brush Red = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
    private static readonly FontFamily Rounded = new("SF Pro Rounded, Segoe UI Variable Display, Segoe UI");

    private readonly ILlmUsageService _service;
    private readonly UniformGrid _cards = new() { Rows = 1 };

    public LlmUsageTabContent(ShellViewModel shell)
    {
        _service = shell.LlmUsage;
        Content = new Border { Padding = new Thickness(12, 2, 12, 10), Child = _cards };

        Loaded += (_, _) =>
        {
            _service.Changed += OnChanged;
            Rebuild();
            _ = _service.RefreshAsync();
        };
        Unloaded += (_, _) => _service.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Rebuild);

    private void Rebuild()
    {
        _cards.Children.Clear();
        var results = _service.Latest;
        if (results.Count == 0)
        {
            _cards.Children.Add(Caption("No providers enabled", Secondary, center: true));
            return;
        }
        foreach (var r in results)
            _cards.Children.Add(Card(r));
    }

    private FrameworkElement Card(ProviderUsage usage)
    {
        var body = new StackPanel();

        // ── header: name + plan badge ───────────────────────────────────
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
        header.Children.Add(new TextBlock
        {
            Text = usage.Provider == UsageProviderId.Claude ? "Claude" : "Codex",
            FontSize = 14.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (usage.Snapshot?.Plan is { Length: > 0 } plan)
            header.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(7), Background = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = plan, FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Secondary },
            });
        body.Children.Add(header);

        // ── content ─────────────────────────────────────────────────────
        if (usage.IsLoading)
            body.Children.Add(Caption("Loading…", Secondary));
        else if (usage.Error is { } error)
            body.Children.Add(Caption(error, Secondary));
        else if (usage.Snapshot is { } s)
        {
            if (s.SessionLimit is null && s.WeekLimit is null)
            {
                body.Children.Add(WindowRow("Today", s.Today, prominent: true));
                body.Children.Add(WindowRow("Week", s.Week));
                body.Children.Add(WindowRow("Session", s.Session));
                var note = Caption("quota unavailable", Tertiary);
                note.Margin = new Thickness(0, 5, 0, 0);
                body.Children.Add(note);
            }
            else
            {
                if (s.SessionLimit is { } sl) body.Children.Add(Gauge("Session", sl));
                if (s.WeekLimit is { } wl) body.Children.Add(Gauge("Week", wl));
                var compact = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
                compact.Children.Add(WindowRow("Today", s.Today, compact: true));
                compact.Children.Add(WindowRow("Week", s.Week, compact: true));
                body.Children.Add(compact);
            }
        }

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = CardBg,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(4, 0, 4, 0),
            Child = body,
            ToolTip = "Estimated API-equivalent cost from local token counts — not your subscription bill.",
        };
    }

    /// <summary>"Today  1.2M  ·  $3.40" — label, tokens, cost.</summary>
    private static FrameworkElement WindowRow(string label, UsageTotals totals, bool prominent = false, bool compact = false)
    {
        var grid = new Grid { Margin = new Thickness(0, prominent ? 0 : 2, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 50 : 52) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var name = new TextBlock { Text = label, FontSize = 10.5, Foreground = Secondary, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, prominent ? 2 : 0) };
        var value = new TextBlock
        {
            Text = Tokens(totals.TotalTokens),
            FontFamily = Rounded,
            FontSize = compact ? 11.5 : prominent ? 18 : 13,
            FontWeight = prominent ? FontWeights.Bold : FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        System.Windows.Documents.Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
        var cost = new TextBlock
        {
            Text = CostLabel(totals), FontSize = 10.5, Foreground = Secondary,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, prominent ? 2 : 0),
        };
        Grid.SetColumn(value, 1);
        Grid.SetColumn(cost, 2);
        grid.Children.Add(name);
        grid.Children.Add(value);
        grid.Children.Add(cost);
        return grid;
    }

    /// <summary>quotaGauge: label + "resets in …", a 6pt capsule (blue, orange
    /// above 90%, red above 95%), then "% used" / "% left".</summary>
    private static FrameworkElement Gauge(string label, UsageLimit limit)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

        var top = new Grid();
        top.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Secondary });
        if (ResetsIn(limit.ResetsAt) is { } resets)
            top.Children.Add(new TextBlock { Text = resets, FontSize = 10, Foreground = Tertiary, HorizontalAlignment = HorizontalAlignment.Right });
        panel.Children.Add(top);

        var fill = new Border
        {
            CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left,
            Background = limit.Fraction > 0.95 ? Red : limit.Fraction > 0.9 ? Orange : Blue,
        };
        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Track, Margin = new Thickness(0, 4, 0, 3), Child = fill };
        track.SizeChanged += (_, _) => fill.Width = Math.Max(4, track.ActualWidth * limit.Fraction);
        panel.Children.Add(track);

        var used = (int)Math.Round(limit.UsedPercent);
        var bottom = new Grid();
        bottom.Children.Add(new TextBlock { Text = $"{used}% used", FontSize = 10, Foreground = Brushes.White });
        bottom.Children.Add(new TextBlock { Text = $"{Math.Max(0, 100 - used)}% left", FontSize = 10, Foreground = Secondary, HorizontalAlignment = HorizontalAlignment.Right });
        panel.Children.Add(bottom);
        return panel;
    }

    private static TextBlock Caption(string text, Brush brush, bool center = false) => new()
    {
        Text = text, FontSize = 11, Foreground = brush, TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
        VerticalAlignment = center ? VerticalAlignment.Center : VerticalAlignment.Top,
    };

    public static string Tokens(long n) => n switch
    {
        >= 1_000_000 => (n / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (n / 1_000.0).ToString("0.0", CultureInfo.InvariantCulture) + "k",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>"$3.40", "$3.40+" when some models are unpriced, "est. n/a" when none are.</summary>
    public static string CostLabel(UsageTotals t)
    {
        var money = t.CostUsd.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        if (!t.HasUnpricedModel) return money;
        return t.CostUsd > 0 ? money + "+" : "est. n/a";
    }

    private static string? ResetsIn(DateTimeOffset? at)
    {
        if (at is null) return null;
        var s = (int)(at.Value - DateTimeOffset.Now).TotalSeconds;
        if (s <= 0) return null;
        int days = s / 86400, hours = s / 3600, minutes = s % 3600 / 60;
        if (days > 0) return $"resets in {days}d {hours % 24}h";
        return hours > 0 ? $"resets in {hours}h {minutes}m" : $"resets in {minutes}m";
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
