using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Album-art colour extraction, mirroring the original <c>quickAverageColor</c>
/// (downscale to ~32px and average) and <c>spectrogramGradient</c> (a top-down
/// gradient from that colour to its complementary hue). Results are cached by
/// artwork byte[] reference, since the media tick re-emits the same array.
/// </summary>
public static class AlbumPalette
{
    private static readonly Color FallbackAvg = Color.FromRgb(0x8E, 0x8E, 0x93);

    private static byte[]? _lastBytes;
    private static Color _lastAvg = FallbackAvg;
    private static Brush? _lastGradient;

    /// <summary>Average colour of the artwork (brightened enough to read on black).</summary>
    public static Color Average(byte[]? artwork)
    {
        Compute(artwork);
        return _lastAvg;
    }

    /// <summary>Top-to-bottom gradient: average colour → complementary hue.</summary>
    public static Brush Gradient(byte[]? artwork)
    {
        Compute(artwork);
        return _lastGradient!;
    }

    private static void Compute(byte[]? artwork)
    {
        if (_lastGradient is not null && ReferenceEquals(artwork, _lastBytes)) return;

        var avg = artwork is { Length: > 0 } ? AverageOf(artwork) ?? FallbackAvg : FallbackAvg;
        avg = EnsureMinimumBrightness(avg, 0.6);
        var comp = Complement(avg);

        var brush = new LinearGradientBrush(avg, comp, 90.0); // 90° = top → bottom
        brush.Freeze();

        _lastBytes = artwork;
        _lastAvg = avg;
        _lastGradient = brush;
    }

    private static Color? AverageOf(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 32;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();

            var src = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            src.CopyPixels(px, stride, 0);

            long r = 0, g = 0, b = 0, n = 0;
            for (int i = 0; i + 3 < px.Length; i += 4)
            {
                b += px[i]; g += px[i + 1]; r += px[i + 2]; n++;
            }
            if (n == 0) return null;
            return Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }
        catch
        {
            return null;
        }
    }

    private static Color Complement(Color c)
    {
        RgbToHsv(c, out var h, out var s, out var v);
        return HsvToRgb((h + 180.0) % 360.0, s, v);
    }

    private static Color EnsureMinimumBrightness(Color c, double minV)
    {
        RgbToHsv(c, out var h, out var s, out var v);
        return v >= minV ? c : HsvToRgb(h, s, minV);
    }

    private static void RgbToHsv(Color c, out double h, out double s, out double v)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        v = max;
        s = max <= 0 ? 0 : d / max;
        if (d <= 1e-9) { h = 0; return; }
        if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * (((b - r) / d) + 2);
        else h = 60 * (((r - g) / d) + 4);
        if (h < 0) h += 360;
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c;
        double r, g, b;
        if (h < 60) (r, g, b) = (c, x, 0);
        else if (h < 120) (r, g, b) = (x, c, 0);
        else if (h < 180) (r, g, b) = (0, c, x);
        else if (h < 240) (r, g, b) = (0, x, c);
        else if (h < 300) (r, g, b) = (x, 0, c);
        else (r, g, b) = (c, 0, x);
        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
