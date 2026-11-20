namespace Atoll.Core.Models;

/// <summary>
/// Canonical media playback model, translated from <c>MusicManager</c>
/// published state (songTitle, artistName, albumArt, isPlaying, ...).
/// One instance represents "what is currently playing / last known".
/// </summary>
public record MediaPlaybackState
{
    public string? PlayerAppUserModelId { get; init; }
    public string? BundleIdentifier { get; init; }

    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }

    public Uri? ArtworkUri { get; init; }
    public byte[]? ArtworkBytes { get; init; }

    public bool IsPlaying { get; init; }
    public bool IsPlayerIdle { get; init; } = true;
    public bool IsAdvertisement { get; init; }

    public TimeSpan Duration { get; init; }
    public TimeSpan Position { get; init; }
    public double PlaybackRate { get; init; } = 1.0;

    /// <summary>Clock text for the elapsed position (m:ss, or h:mm:ss).</summary>
    public string PositionText => FormatClock(Position);

    /// <summary>Clock text for the track duration (m:ss, or h:mm:ss).</summary>
    public string DurationText => FormatClock(Duration);

    /// <summary>Remaining time as a negative clock (e.g. -2:56), matching the
    /// original's trailing time label.</summary>
    public string RemainingText
    {
        get
        {
            var remaining = Duration - Position;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            return "-" + FormatClock(remaining);
        }
    }

    /// <summary>Playback progress in [0,1]; 0 when the duration is unknown.</summary>
    public double Progress
    {
        get
        {
            var total = Duration.TotalSeconds;
            if (total <= 0) return 0;
            return Math.Clamp(Position.TotalSeconds / total, 0.0, 1.0);
        }
    }

    private static string FormatClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes}:{value.Seconds:00}";
    }

    public bool IsShuffled { get; init; }
    public MediaRepeatMode RepeatMode { get; init; } = MediaRepeatMode.Off;
    public bool IsLiveStream { get; init; }

    public bool? IsLiked { get; init; }
    public bool CanFavorite { get; init; }

    public string? CurrentLyrics { get; init; }
    public bool LyricsAvailable { get; init; }

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum MediaRepeatMode
{
    Off,
    One,
    All
}

/// <summary>
/// Media transport command.
/// </summary>
public enum MediaCommand
{
    PlayPause,
    Play,
    Pause,
    Next,
    Previous,
    Stop,
    SeekForward,
    SeekBackward,
    ToggleShuffle,
    ToggleRepeat,
    SetVolume,
    Like
}
