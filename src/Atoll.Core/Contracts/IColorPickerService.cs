using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// On-screen color picker. Translated from <c>ColorPickerManager</c> and
/// <c>PickedColor</c>.
/// </summary>
public interface IColorPickerService : IDisposable
{
    event EventHandler<PickedColor>? ColorPicked;
    IReadOnlyList<PickedColor> History { get; }
    int MaxHistorySize { get; set; }

    /// <summary>Sample the screen color at a point (interactive picker).</summary>
    Task<PickedColor?> PickAtScreenPointAsync(int screenX, int screenY, CancellationToken ct = default);
    void AddToHistory(PickedColor color);
    void ClearHistory();
}

public sealed class PickedColor
{
    public string Hex { get; init; } = string.Empty;
    public byte R { get; init; }
    public byte G { get; init; }
    public byte B { get; init; }
    public double Hue { get; init; }
    public double Saturation { get; init; }
    public double Lightness { get; init; }
    public string Format { get; init; } = "hex";
    public DateTimeOffset PickedAt { get; init; } = DateTimeOffset.UtcNow;
}
