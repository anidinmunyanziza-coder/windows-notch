using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Models;

namespace Atoll.Core.Contracts;

/// <summary>
/// Media playback integration. Translated from <c>MusicManager</c>,
/// <c>SystemMediaControllers</c>, and <c>MediaControllers/*</c>.
/// Windows implementation is built on GSMTC plus per-player adapters (Spotify Web API, etc.).
/// </summary>
public interface IMediaService : IDisposable
{
    /// <summary>Raised when the canonical playback state changes.</summary>
    event EventHandler<MediaPlaybackState>? StateChanged;

    /// <summary>Currently known playback state.</summary>
    MediaPlaybackState CurrentState { get; }

    /// <summary>Enumerate detected media sessions.</summary>
    Task<IReadOnlyList<MediaSessionInfo>> GetSessionsAsync(CancellationToken ct = default);

    /// <summary>Send a transport command to the active/selected player.</summary>
    Task<bool> SendCommandAsync(MediaCommand command, CancellationToken ct = default);

    /// <summary>Seek to a position.</summary>
    Task<bool> SeekAsync(TimeSpan position, CancellationToken ct = default);

    /// <summary>Set volume (0.0-1.0).</summary>
    Task<bool> SetVolumeAsync(double volume, CancellationToken ct = default);

    /// <summary>Toggle like/favorite on the current track (player-dependent).</summary>
    Task<bool> ToggleLikeAsync(CancellationToken ct = default);

    /// <summary>Switch the active player by AppUserModelId / bundle id.</summary>
    Task<bool> SwitchPlayerAsync(string playerAppUserModelId, CancellationToken ct = default);

    /// <summary>Fetch artwork bytes for a URI.</summary>
    Task<byte[]?> GetArtworkAsync(Uri? artworkUri, CancellationToken ct = default);

    /// <summary>Start/stop background monitoring.</summary>
    Task StartMonitoringAsync(CancellationToken ct = default);
    Task StopMonitoringAsync(CancellationToken ct = default);
    bool IsMonitoring { get; }
}

public sealed class MediaSessionInfo
{
    public string? AppUserModelId { get; init; }
    public string? PlayerName { get; init; }
    public bool IsActive { get; init; }
    public bool IsSource { get; init; }
}
