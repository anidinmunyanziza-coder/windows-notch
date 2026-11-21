using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Atoll.Core.Models;
using Atoll.Core.ViewModels;
using Atoll.Platform.Windows.Animation;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Media player tab content for the notch home view — album art, track info, a
/// progress scrubber and transport controls. Mirrors the original NotchHomeView
/// / MinimalisticMusicPlayerView: the art dims to 40% and shrinks to 85% while
/// paused, carries an album-tinted glow, and is the matched-geometry target the
/// closed pill's art morphs into.
/// </summary>
public partial class HomeTabContent : UserControl
{
    private ShellViewModel? Shell => DataContext as ShellViewModel;
    private ShellViewModel? _subscribed;
    private bool? _wasPlaying;
    private byte[]? _glowArt;

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

        // Album-tinted glow behind the art (re-tint only when the art changes).
        var art = media?.ArtworkBytes;
        if (!ReferenceEquals(art, _glowArt))
        {
            _glowArt = art;
            ArtImage.Effect = art is { Length: > 0 }
                ? new DropShadowEffect
                {
                    Color = AlbumPalette.Average(art),
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.35,
                }
                : null;
        }
    }

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
}
