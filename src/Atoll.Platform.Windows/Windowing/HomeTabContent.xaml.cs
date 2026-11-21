using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Atoll.Core.Models;
using Atoll.Core.ViewModels;
using Atoll.Platform.Windows.Animation;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Now Playing for the notch home view, styled after Apple's own players (the
/// iPhone Dynamic Island expanded view and macOS Control Center): rounded
/// artwork with an album-tinted glow, title over secondary-grey artist, an
/// album-coloured waveform, a hairline scrubber that thickens on hover and can be
/// dragged to seek, and SF-Symbol-style transport glyphs. The art dims to 40% and
/// shrinks to 85% while paused (as in the original) and is the matched-geometry
/// target the closed pill's art morphs into.
/// </summary>
public partial class HomeTabContent : UserControl
{
    /// <summary>Corner radius of the open artwork; the notch morph flies to it.</summary>
    public const double ArtCornerRadius = 12;

    private static readonly Brush ActiveToggle = Frozen(Colors.White);
    private static readonly Brush InactiveToggle = Frozen(Color.FromRgb(0x8E, 0x8E, 0x93));

    private ShellViewModel? Shell => DataContext as ShellViewModel;
    private ShellViewModel? _subscribed;
    private bool? _wasPlaying;
    private bool? _glyphPlaying;
    private byte[]? _glowArt;

    // scrubber
    private bool _dragging;
    private double _dragFraction;
    private DateTime _seekHoldUntil = DateTime.MinValue;

    public HomeTabContent()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Shell is { } s && !ReferenceEquals(_subscribed, s))
            {
                _subscribed = s;
                s.PropertyChanged += OnShellChanged;
            }
            Sync(animate: false);
        };
        Unloaded += (_, _) =>
        {
            if (_subscribed is not null) _subscribed.PropertyChanged -= OnShellChanged;
            _subscribed = null;
        };

        Track.SizeChanged += (_, _) => UpdateProgress(Shell?.MediaState);
        ScrubArea.MouseEnter += (_, _) => SetScrubberExpanded(true);
        ScrubArea.MouseLeave += (_, _) => { if (!_dragging) SetScrubberExpanded(false); };
        ScrubArea.MouseLeftButtonDown += OnScrubDown;
        ScrubArea.MouseMove += OnScrubMove;
        ScrubArea.MouseLeftButtonUp += OnScrubUp;
        ScrubArea.LostMouseCapture += (_, _) =>
        {
            _dragging = false;
            if (!ScrubArea.IsMouseOver) SetScrubberExpanded(false);
        };
    }

    // Matched-geometry endpoints for the notch morph.
    public FrameworkElement ArtMorphHost => ArtHost;
    public FrameworkElement ArtMorphVisual => ArtVisual;
    public FrameworkElement SpectrumMorphHost => SpectrumHost;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellViewModel.MediaState)) return;
        // MediaState is published from the media service's timer thread.
        if (Dispatcher.CheckAccess())
            Sync(animate: true);
        else
            Dispatcher.BeginInvoke(() => Sync(animate: true));
    }

    private void Sync(bool animate)
    {
        var media = Shell?.MediaState;
        var playing = media?.IsPlaying == true;

        if (_wasPlaying != playing)
        {
            _wasPlaying = playing;
            var opacity = playing ? 1.0 : 0.4;
            var scale = playing ? 1.0 : 0.85;
            if (animate)
            {
                var dur = TimeSpan.FromSeconds(0.42);
                var ease = new SpringEasing { DampingFraction = 0.8 };
                ArtVisual.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(opacity, dur) { EasingFunction = ease });
                ArtScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(scale, dur) { EasingFunction = ease });
                ArtScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(scale, dur) { EasingFunction = ease });
            }
            else
            {
                ArtVisual.Opacity = opacity;
                ArtScale.ScaleX = scale;
                ArtScale.ScaleY = scale;
            }
        }

        SetPlayGlyph(playing, animate);

        // Centre the title on the artwork when there is no artist line.
        ArtistText.Visibility = string.IsNullOrEmpty(media?.Artist) ? Visibility.Collapsed : Visibility.Visible;

        // Shuffle / repeat: secondary grey when off, white on a soft tile when on.
        var shuffled = media?.IsShuffled == true;
        ShuffleButton.Foreground = shuffled ? ActiveToggle : InactiveToggle;
        ShuffleActiveBg.Visibility = shuffled ? Visibility.Visible : Visibility.Collapsed;

        var repeat = media?.RepeatMode ?? MediaRepeatMode.Off;
        RepeatButton.Foreground = repeat == MediaRepeatMode.Off ? InactiveToggle : ActiveToggle;
        RepeatActiveBg.Visibility = repeat == MediaRepeatMode.Off ? Visibility.Collapsed : Visibility.Visible;
        RepeatOneBadge.Visibility = repeat == MediaRepeatMode.One ? Visibility.Visible : Visibility.Collapsed;

        UpdateProgress(media);

        // Album-tinted glow behind the art (re-tint only when the art changes).
        var art = media?.ArtworkBytes;
        if (!ReferenceEquals(art, _glowArt))
        {
            _glowArt = art;
            ArtImage.Effect = art is { Length: > 0 }
                ? new DropShadowEffect
                {
                    Color = AlbumPalette.Average(art),
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.45,
                }
                : null;
        }
    }

    /// <summary>play.fill ⇄ pause.fill with the SF Symbols "replace" feel:
    /// the outgoing glyph shrinks and fades as the incoming one grows in.</summary>
    private void SetPlayGlyph(bool playing, bool animate)
    {
        if (_glyphPlaying == playing) return;
        _glyphPlaying = playing;

        var (show, showScale, hide, hideScale) = playing
            ? (PauseGlyph, PauseScale, PlayGlyph, PlayScale)
            : (PlayGlyph, PlayScale, PauseGlyph, PauseScale);

        if (!animate)
        {
            show.Opacity = 1; showScale.ScaleX = showScale.ScaleY = 1;
            hide.Opacity = 0; hideScale.ScaleX = hideScale.ScaleY = 0.6;
            return;
        }

        var dur = TimeSpan.FromSeconds(0.32);
        var ease = new SpringEasing { DampingFraction = 0.75 };
        show.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromSeconds(0.18)));
        showScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1, dur) { EasingFunction = ease });
        showScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1, dur) { EasingFunction = ease });
        hide.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromSeconds(0.14)));
        hideScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, TimeSpan.FromSeconds(0.14)));
        hideScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, TimeSpan.FromSeconds(0.14)));
    }

    // ------------------------------------------------------------------
    // Scrubber
    // ------------------------------------------------------------------

    private void UpdateProgress(MediaPlaybackState? media)
    {
        var duration = media?.Duration ?? TimeSpan.Zero;
        double fraction;
        TimeSpan elapsed;

        if (_dragging || DateTime.UtcNow < _seekHoldUntil)
        {
            // While dragging (and briefly after a seek, until the player
            // reports the new position) show the scrub position, not the old one.
            fraction = _dragFraction;
            elapsed = TimeSpan.FromTicks((long)(duration.Ticks * fraction));
        }
        else
        {
            fraction = media?.Progress ?? 0;
            elapsed = media?.Position ?? TimeSpan.Zero;
        }

        TrackFill.Width = Math.Max(0, Track.ActualWidth * Math.Clamp(fraction, 0, 1));
        ElapsedText.Text = Clock(elapsed);
        var remaining = duration - elapsed;
        RemainingText.Text = "-" + Clock(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining);
    }

    private void SetScrubberExpanded(bool expanded)
    {
        // Apple's scrubbers thicken under the pointer; times brighten with them.
        var ease = new SpringEasing { DampingFraction = 0.9 };
        Track.BeginAnimation(HeightProperty,
            new DoubleAnimation(expanded ? 8 : 5, TimeSpan.FromSeconds(0.25)) { EasingFunction = ease });
        var timeBrush = expanded ? Brushes.White : InactiveToggle;
        ElapsedText.Foreground = timeBrush;
        RemainingText.Foreground = timeBrush;
        TrackFill.Background = expanded ? Brushes.White : Frozen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
    }

    private void OnScrubDown(object sender, MouseButtonEventArgs e)
    {
        if (Shell?.MediaState is not { Duration.Ticks: > 0 }) return;
        _dragging = true;
        ScrubArea.CaptureMouse();
        SetScrubberExpanded(true);
        UpdateDrag(e);
        e.Handled = true; // don't let the click toggle the notch
    }

    private void OnScrubMove(object sender, MouseEventArgs e)
    {
        if (_dragging) UpdateDrag(e);
    }

    private async void OnScrubUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        e.Handled = true;
        UpdateDrag(e);
        _dragging = false;
        ScrubArea.ReleaseMouseCapture();
        if (Shell is not { MediaState: { } media } shell) return;

        _seekHoldUntil = DateTime.UtcNow.AddSeconds(1.2);
        await shell.SeekMedia(TimeSpan.FromTicks((long)(media.Duration.Ticks * _dragFraction)));
    }

    private void UpdateDrag(MouseEventArgs e)
    {
        var w = Track.ActualWidth;
        if (w <= 0) return;
        _dragFraction = Math.Clamp(e.GetPosition(Track).X / w, 0, 1);
        UpdateProgress(Shell?.MediaState);
    }

    private static string Clock(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    // ------------------------------------------------------------------
    // Transport
    // ------------------------------------------------------------------

    private async void OnShuffle(object sender, RoutedEventArgs e)
    {
        if (Shell is not null) await Shell.SendMediaCommand(MediaCommand.ToggleShuffle);
    }

    private async void OnPrevious(object sender, RoutedEventArgs e)
    {
        if (Shell is not null) await Shell.SendMediaCommand(MediaCommand.Previous);
    }

    private async void OnPlayPause(object sender, RoutedEventArgs e)
    {
        if (Shell is not null) await Shell.SendMediaCommand(MediaCommand.PlayPause);
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (Shell is not null) await Shell.SendMediaCommand(MediaCommand.Next);
    }

    private async void OnRepeat(object sender, RoutedEventArgs e)
    {
        if (Shell is not null) await Shell.SendMediaCommand(MediaCommand.ToggleRepeat);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
