using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Converts the media session's raw artwork bytes into a decoded, frozen
/// <see cref="BitmapImage"/> for the notch album-art slot. Returns null when
/// there is no artwork, so the caller can show a placeholder.
/// </summary>
public sealed class ArtworkConverter : IValueConverter
{
    // The position tick emits a new state ~4x/sec reusing the same artwork byte[]
    // reference; cache the decoded image by that reference so it is decoded once
    // per track, not on every tick.
    private byte[]? _lastBytes;
    private BitmapImage? _lastImage;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes) return null;
        if (ReferenceEquals(bytes, _lastBytes)) return _lastImage;

        try
        {
            var image = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();

            _lastBytes = bytes;
            _lastImage = image;
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True when the bound value is null (used to show the art placeholder
/// only when there is no artwork).</summary>
public sealed class IsNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Maps IsPlaying to the Segoe MDL2 pause (playing) or play glyph.</summary>
public sealed class PlayPauseGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "" : ""; // Pause : Play

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Tints a control accent-blue when the bound bool is true, else the
/// 85%-white idle colour — the shuffle active state.</summary>
public sealed class BoolToAccentBrushConverter : IValueConverter
{
    internal static readonly System.Windows.Media.SolidColorBrush Accent =
        new(System.Windows.Media.Color.FromRgb(0x0A, 0x84, 0xFF));
    internal static readonly System.Windows.Media.SolidColorBrush Idle =
        new(System.Windows.Media.Color.FromArgb(0xD9, 0xFF, 0xFF, 0xFF));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Accent : Idle;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Repeat mode to its Segoe MDL2 glyph (RepeatOne for One, else RepeatAll).</summary>
public sealed class RepeatGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Atoll.Core.Models.MediaRepeatMode.One ? "" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Repeat mode to brush: accent when repeating (One/All), idle when Off.</summary>
public sealed class RepeatBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Atoll.Core.Models.MediaRepeatMode.Off or null
            ? BoolToAccentBrushConverter.Idle
            : BoolToAccentBrushConverter.Accent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
