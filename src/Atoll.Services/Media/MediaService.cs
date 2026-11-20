using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Atoll.Core.Contracts;
using Atoll.Core.Models;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Timer = System.Timers.Timer;

namespace Atoll.Services.Media;

/// <summary>
/// Windows media integration built on Global System Media Transport Controls
/// (GSMTC). Translates the original <c>MusicManager</c> / <c>SystemMediaControllers</c>
/// / per-player adapters to Windows: play/pause, next/prev, seek, track metadata,
/// artwork, playback status, shuffle/repeat.
/// </summary>
public sealed class MediaService : IMediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private GlobalSystemMediaTransportControlsSession? _subscribed;
    private MediaPlaybackState _state = new();
    private readonly Timer _refreshTimer;
    private readonly object _sync = new();

    // Cached track metadata (title/artist/album/artwork). Refreshed only when the
    // player reports a media-properties change, so the 4x/sec position tick never
    // re-reads or re-decodes artwork.
    private string? _metaTitle;
    private string? _metaArtist;
    private string? _metaAlbum;
    private byte[]? _metaArtwork;

    public event EventHandler<MediaPlaybackState>? StateChanged;

    public MediaPlaybackState CurrentState
    {
        get => _state;
        private set
        {
            _state = value;
            StateChanged?.Invoke(this, _state);
        }
    }

    public bool IsMonitoring { get; private set; }

    public MediaService()
    {
        // Tick ~4x/sec. Each tick reads only the lightweight playback/timeline
        // info and interpolates the elapsed position from the wall clock, so the
        // progress bar advances smoothly between the player's own updates.
        _refreshTimer = new Timer(250) { AutoReset = true, Enabled = false };
        _refreshTimer.Elapsed += (_, _) => Tick();
    }

    public async Task StartMonitoringAsync(CancellationToken ct = default)
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();

            // Re-pick the best session whenever sessions appear/disappear or the
            // system's current session changes. Without this the notch stays on
            // "No track" when playback starts after launch (the initial select
            // found nothing and never re-ran).
            _manager.SessionsChanged += (_, _) => _ = SelectBestSessionAsync();
            _manager.CurrentSessionChanged += (_, _) => _ = SelectBestSessionAsync();

            await SelectBestSessionAsync();
            IsMonitoring = true;
            _refreshTimer.Enabled = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] media monitoring start failed: {ex.Message}");
            IsMonitoring = false;
        }
    }

    public Task StopMonitoringAsync(CancellationToken ct = default)
    {
        IsMonitoring = false;
        _refreshTimer.Enabled = false;
        lock (_sync) { _session = null; }
        CurrentState = new MediaPlaybackState { IsPlayerIdle = true };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MediaSessionInfo>> GetSessionsAsync(CancellationToken ct = default)
    {
        var result = new List<MediaSessionInfo>();
        try
        {
            var mgr = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
            foreach (var s in mgr.GetSessions())
                result.Add(new MediaSessionInfo
                {
                    AppUserModelId = s.SourceAppUserModelId,
                    PlayerName = s.SourceAppUserModelId,
                    IsActive = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                    IsSource = true
                });
        }
        catch { /* best effort */ }
        return Task.FromResult<IReadOnlyList<MediaSessionInfo>>(result);
    }

    public async Task<bool> SendCommandAsync(MediaCommand command, CancellationToken ct = default)
    {
        var s = CurrentSession();
        if (s is null) return false;
        try
        {
            return command switch
            {
                MediaCommand.PlayPause => await s.TryTogglePlayPauseAsync().AsTask(),
                MediaCommand.Play => await s.TryPlayAsync().AsTask(),
                MediaCommand.Pause => await s.TryPauseAsync().AsTask(),
                MediaCommand.Next => await s.TrySkipNextAsync().AsTask(),
                MediaCommand.Previous => await s.TrySkipPreviousAsync().AsTask(),
                MediaCommand.Stop => await s.TryStopAsync().AsTask(),
                MediaCommand.SeekForward => await s.TryFastForwardAsync().AsTask(),
                MediaCommand.SeekBackward => await s.TryRewindAsync().AsTask(),
                MediaCommand.ToggleShuffle => await ToggleShuffleAsync(s),
                MediaCommand.ToggleRepeat => await ToggleRepeatAsync(s),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ToggleShuffleAsync(GlobalSystemMediaTransportControlsSession s)
    {
        var info = s.GetPlaybackInfo();
        return await s.TryChangeShuffleActiveAsync(!(info.IsShuffleActive ?? false)).AsTask();
    }

    private static async Task<bool> ToggleRepeatAsync(GlobalSystemMediaTransportControlsSession s)
    {
        var info = s.GetPlaybackInfo();
        var mode = info.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None;
        var next = mode switch
        {
            MediaPlaybackAutoRepeatMode.None => MediaPlaybackAutoRepeatMode.List,
            MediaPlaybackAutoRepeatMode.List => MediaPlaybackAutoRepeatMode.Track,
            _ => MediaPlaybackAutoRepeatMode.None
        };
        return await s.TryChangeAutoRepeatModeAsync(next).AsTask();
    }

    public async Task<bool> SeekAsync(TimeSpan position, CancellationToken ct = default)
    {
        var s = CurrentSession();
        if (s is null) return false;
        try { return await s.TryChangePlaybackPositionAsync(position.Ticks).AsTask(); }
        catch { return false; }
    }

    public Task<bool> SetVolumeAsync(double volume, CancellationToken ct = default)
    {
        // GSMTC does not expose per-session volume in the base API. Volume is
        // handled at the system endpoint (WASAPI) — see Platform.Windows audio.
        return Task.FromResult(false);
    }

    public Task<bool> ToggleLikeAsync(CancellationToken ct = default)
    {
        // GSMTC has no generic like/favorite; per-player APIs (Spotify Web API,
        // Apple Music) handle that. False -> UI shows like as unavailable.
        return Task.FromResult(false);
    }

    public Task<bool> SwitchPlayerAsync(string playerAppUserModelId, CancellationToken ct = default)
    {
        if (_manager is null) return Task.FromResult(false);
        foreach (var s in _manager.GetSessions())
        {
            if (string.Equals(s.SourceAppUserModelId, playerAppUserModelId, StringComparison.OrdinalIgnoreCase))
            {
                lock (_sync) { _session = s; }
                SubscribeToSession(s);
                _ = RefreshMetadataAsync(s);
                return Task.FromResult(true);
            }
        }
        return Task.FromResult(false);
    }

    public async Task<byte[]?> GetArtworkAsync(Uri? artworkUri, CancellationToken ct = default)
    {
        if (artworkUri is null) return null;
        try
        {
            var refStream = RandomAccessStreamReference.CreateFromUri(artworkUri);
            var stream = await refStream.OpenReadAsync().AsTask();
            using var ms = new MemoryStream();
            stream.AsStreamForRead().CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------
    // Internals (polling-based)
    // ------------------------------------------------------------------

    private async Task SelectBestSessionAsync()
    {
        if (_manager is null) return;
        var sessions = _manager.GetSessions();
        GlobalSystemMediaTransportControlsSession? best = null;
        foreach (var s in sessions)
        {
            var status = s.GetPlaybackInfo().PlaybackStatus;
            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            { best = s; break; }
            best ??= s;
        }

        lock (_sync) { _session = best; }

        if (best is null)
        {
            CurrentState = new MediaPlaybackState { IsPlayerIdle = true };
            return;
        }

        SubscribeToSession(best);
        await RefreshMetadataAsync(best);
        _refreshTimer.Enabled = true;
        Tick();
    }

    private GlobalSystemMediaTransportControlsSession? CurrentSession()
    {
        lock (_sync) { return _session; }
    }

    /// <summary>Subscribe to the session's media-properties changes so the cached
    /// metadata/artwork refreshes on track change (not on every tick).</summary>
    private void SubscribeToSession(GlobalSystemMediaTransportControlsSession session)
    {
        if (ReferenceEquals(_subscribed, session)) return;

        if (_subscribed is not null)
            _subscribed.MediaPropertiesChanged -= OnMediaPropertiesChanged;

        _subscribed = session;
        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        => _ = RefreshMetadataAsync(sender);

    /// <summary>Fetch and cache the heavy track metadata + artwork once per track.</summary>
    private async Task RefreshMetadataAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            _metaTitle = props.Title;
            _metaArtist = props.Artist;
            _metaAlbum = props.AlbumTitle;

            byte[]? art = null;
            if (props.Thumbnail is not null)
            {
                try
                {
                    var stream = await props.Thumbnail.OpenReadAsync().AsTask();
                    using var ms = new MemoryStream();
                    stream.AsStreamForRead().CopyTo(ms);
                    art = ms.ToArray();
                }
                catch { /* artwork unavailable */ }
            }
            _metaArtwork = art;

            Tick();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] media metadata refresh failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Lightweight ~4x/sec update: reads playback/timeline info (no async, no
    /// artwork) and interpolates the elapsed position from the wall clock using
    /// the timeline's last-updated timestamp, so the progress bar advances
    /// between the player's sparse timeline reports (mirrors MusicManager's
    /// elapsedTime bookkeeping).
    /// </summary>
    private void Tick()
    {
        var s = CurrentSession();
        if (s is null)
        {
            CurrentState = new MediaPlaybackState { IsPlayerIdle = true };
            return;
        }

        try
        {
            var playback = s.GetPlaybackInfo();
            var timeline = s.GetTimelineProperties();
            var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var rate = playback.PlaybackRate ?? 1.0;
            var duration = timeline.EndTime - timeline.StartTime;

            var position = timeline.Position - timeline.StartTime;
            if (playing)
            {
                var sinceUpdate = DateTimeOffset.Now - timeline.LastUpdatedTime;
                if (sinceUpdate > TimeSpan.Zero)
                    position += TimeSpan.FromTicks((long)(sinceUpdate.Ticks * rate));
            }

            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (duration > TimeSpan.Zero && position > duration) position = duration;

            CurrentState = new MediaPlaybackState
            {
                PlayerAppUserModelId = s.SourceAppUserModelId,
                BundleIdentifier = s.SourceAppUserModelId,
                Title = _metaTitle,
                Artist = _metaArtist,
                Album = _metaAlbum,
                ArtworkBytes = _metaArtwork,
                IsPlaying = playing,
                IsPlayerIdle = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed,
                IsAdvertisement = false,
                Duration = duration,
                Position = position,
                PlaybackRate = rate,
                IsShuffled = playback.IsShuffleActive ?? false,
                RepeatMode = playback.AutoRepeatMode switch
                {
                    MediaPlaybackAutoRepeatMode.Track => MediaRepeatMode.One,
                    MediaPlaybackAutoRepeatMode.List => MediaRepeatMode.All,
                    _ => MediaRepeatMode.Off
                },
                IsLiveStream = false,
                CanFavorite = false,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] media tick failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _refreshTimer.Dispose();
        StopMonitoringAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
