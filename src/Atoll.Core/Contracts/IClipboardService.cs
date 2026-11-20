using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Atoll.Core.Contracts;

/// <summary>
/// Clipboard history capture + persistence. Translated from
/// <c>ClipboardManager</c> (ContentChanged listener, dedupe, history size,
/// persistClipboardHistory, copyOnDrag).
/// </summary>
public interface IClipboardService : IDisposable
{
    event EventHandler<ClipboardItem>? ItemAdded;
    event EventHandler<ClipboardItem>? ItemRemoved;
    event EventHandler<ClipboardClearedEventArgs>? Cleared;

    IReadOnlyList<ClipboardItem> History { get; }
    int MaxHistorySize { get; set; }
    bool PersistHistory { get; set; }

    /// <summary>Add current clipboard contents to history (public for tests).</summary>
    Task AddCurrentAsync(CancellationToken ct = default);
    Task SetItemAsync(ClipboardItem item, CancellationToken ct = default);

    /// <summary>Copy a history entry back onto the system clipboard.</summary>
    Task CopyToClipboardAsync(ClipboardItem item, CancellationToken ct = default);

    Task RemoveItemAsync(string itemId, CancellationToken ct = default);
    Task ClearHistoryAsync(CancellationToken ct = default);
    Task StartMonitoringAsync(CancellationToken ct = default);
    Task StopMonitoringAsync(CancellationToken ct = default);
    bool IsMonitoring { get; }
}

/// <summary>
/// Mirrors the original <c>ClipboardItemType</c> (text, url, file, image, rtf,
/// unknown) so icons, labels and copy-back behaviour match the source app.
/// </summary>
public enum ClipboardItemType
{
    Unknown = 0,
    Text,
    Url,
    File,
    Image,
    Rtf,
}

public sealed class ClipboardItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public ClipboardItemType Type { get; init; } = ClipboardItemType.Unknown;
    public string? Text { get; init; }
    public string? Html { get; init; }
    public string? Rtf { get; init; }
    public IEnumerable<string>? FilePaths { get; init; }

    /// <summary>PNG bytes for image items when the image is held in memory.</summary>
    [JsonIgnore]
    public byte[]? ImageBytes { get; set; }

    /// <summary>File name (under the clipboard data directory) for a persisted image.</summary>
    public string? ImageFileName { get; set; }

    public string? SourceApp { get; init; }

    /// <summary>
    /// Preview string used by the notch card. Mirrors the original
    /// <c>ClipboardItem.generatePreview</c>: 50 characters for text, the last
    /// path component (or host) for URLs, a file name / count for files, and a
    /// byte-size description for images.
    /// </summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            switch (Type)
            {
                case ClipboardItemType.Image:
                    return ImageBytes is { Length: > 0 } bytes
                        ? $"Image ({FormatByteCount(bytes.Length)})"
                        : "Image";

                case ClipboardItemType.File:
                    var files = FilePaths?.ToList();
                    if (files is null || files.Count == 0) return "File";
                    if (files.Count == 1) return Path.GetFileName(files[0]);

                    return $"{files.Count} files";

                case ClipboardItemType.Url:
                    if (!string.IsNullOrWhiteSpace(Text) &&
                        Uri.TryCreate(Text, UriKind.Absolute, out var uri))
                    {
                        var path = uri.AbsolutePath.TrimEnd('/');
                        var leaf = path.Length > 0 ? Path.GetFileName(path) : string.Empty;
                        if (!string.IsNullOrEmpty(leaf)) return leaf;
                        if (!string.IsNullOrEmpty(uri.Host)) return uri.Host;
                    }
                    return Truncate(Text);

                default:
                    return Truncate(Text);
            }
        }
    }

    private static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var singleLine = text.Replace("\r", " ").Replace("\n", " ");
        return singleLine.Length <= 50 ? singleLine : singleLine[..50];
    }

    private static string FormatByteCount(long bytes)
    {
        string[] units = { "bytes", "KB", "MB", "GB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} bytes"
            : $"{value:0.#} {units[unit]}";
    }
}

public sealed class ClipboardClearedEventArgs : EventArgs { public bool PersistDisabled { get; init; } }
