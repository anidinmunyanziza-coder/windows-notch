using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Atoll.Core.Contracts;
using WpfClipboard = System.Windows.Clipboard;

namespace Atoll.Services.Productivity.Clipboard;

/// <summary>
/// Clipboard history capture + persistence. Translated from the original
/// <c>ClipboardManager</c> (change-count polling, rich-text/image/file
/// detection, dedupe against the whole history, configurable history size,
/// persistClipboardHistory, copy-back and delete).
///
/// Windows-specific notes:
/// - WPF's clipboard API requires an STA thread, so all clipboard reads and
///   writes are marshalled to one long-lived STA worker.
/// - Change detection uses <c>GetClipboardSequenceNumber</c>, the Win32
///   equivalent of <c>NSPasteboard.changeCount</c>.
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    private static readonly string[] ImageExtensions =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff",
        ".webp", ".heic", ".heif",
    };

    private readonly string _dataDirectory;
    private readonly string _historyFile;
    private readonly object _sync = new();
    private readonly List<ClipboardItem> _history = new();
    private readonly Dictionary<string, byte[]> _inMemoryImages = new();
    private readonly BlockingCollection<Action> _requests = new(new ConcurrentQueue<Action>());
    private readonly Thread _clipboardThread;
    private readonly Timer _pollTimer;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private int _maxHistorySize = 3; // Defaults[.clipboardHistorySize] == 3
    private bool _persistHistory = true; // Defaults[.persistClipboardHistory] == true
    private int _lastSequence;
    private int _captureQueued;
    private volatile bool _monitoring;
    private bool _disposed;

    public event EventHandler<ClipboardItem>? ItemAdded;
    public event EventHandler<ClipboardItem>? ItemRemoved;
    public event EventHandler<ClipboardClearedEventArgs>? Cleared;

    public ClipboardService(string? settingsFolder = null)
    {
        settingsFolder ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Atoll");

        _dataDirectory = Path.Combine(settingsFolder, "ClipboardData");
        Directory.CreateDirectory(settingsFolder);
        Directory.CreateDirectory(_dataDirectory);
        _historyFile = Path.Combine(settingsFolder, "clipboard-history.json");

        LoadHistory();
        CleanupOrphanedImageFiles();
        _lastSequence = unchecked((int)GetClipboardSequenceNumber());

        _clipboardThread = new Thread(ClipboardThreadProc)
        {
            IsBackground = true,
            Name = "Atoll.ClipboardSTA",
        };
        _clipboardThread.SetApartmentState(ApartmentState.STA);
        _clipboardThread.Start();

        _pollTimer = new Timer(OnPoll, null, Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<ClipboardItem> History
    {
        get
        {
            lock (_sync)
            {
                return _history.ToList().AsReadOnly();
            }
        }
    }

    public int MaxHistorySize
    {
        get
        {
            lock (_sync) return _maxHistorySize;
        }
        set
        {
            var size = Math.Max(1, value);
            List<ClipboardItem> removed;
            lock (_sync)
            {
                if (_maxHistorySize == size) return;
                _maxHistorySize = size;
                removed = TrimLocked();
                PersistStateLocked();
            }
            DeleteImageFiles(removed);
        }
    }

    public bool PersistHistory
    {
        get
        {
            lock (_sync) return _persistHistory;
        }
        set
        {
            lock (_sync)
            {
                if (_persistHistory == value) return;
                _persistHistory = value;

                if (value)
                {
                    // Session-only images get a file so they survive the
                    // restart the user just opted into.
                    PersistImagesLocked();
                    WriteHistoryLocked();
                }
                else
                {
                    // Turning persistence off erases what is already on disk,
                    // retaining the bytes in memory for the current session.
                    RetainImagesLocked();
                    TryDeleteHistoryFileLocked();
                }
            }
        }
    }

    public bool IsMonitoring => _monitoring;

    public Task AddCurrentAsync(CancellationToken ct = default)
        => RunOnClipboardThread(CaptureAndAdd, ct);

    public Task SetItemAsync(ClipboardItem item, CancellationToken ct = default)
    {
        AddItemInternal(item);
        return Task.CompletedTask;
    }

    public Task CopyToClipboardAsync(ClipboardItem item, CancellationToken ct = default)
        => RunOnClipboardThread(() =>
        {
            var data = BuildDataObject(item);
            WpfClipboard.SetDataObject(data, true);
            Volatile.Write(ref _lastSequence, unchecked((int)GetClipboardSequenceNumber()));
        }, ct);

    public Task RemoveItemAsync(string itemId, CancellationToken ct = default)
    {
        ClipboardItem? removed = null;
        lock (_sync)
        {
            var index = _history.FindIndex(i => i.Id == itemId);
            if (index < 0) return Task.CompletedTask;

            removed = _history[index];
            _history.RemoveAt(index);
            PersistStateLocked();
        }

        DeleteImageFiles(new[] { removed });
        ItemRemoved?.Invoke(this, removed);
        return Task.CompletedTask;
    }

    public Task ClearHistoryAsync(CancellationToken ct = default)
    {
        List<ClipboardItem> cleared;
        bool persistDisabled;
        lock (_sync)
        {
            cleared = _history.ToList();
            _history.Clear();
            _inMemoryImages.Clear();
            persistDisabled = !_persistHistory;

            if (_persistHistory) WriteHistoryLocked();
            else TryDeleteHistoryFileLocked();
        }

        DeleteImageFiles(cleared);
        Cleared?.Invoke(this, new ClipboardClearedEventArgs { PersistDisabled = persistDisabled });
        return Task.CompletedTask;
    }

    public Task StartMonitoringAsync(CancellationToken ct = default)
    {
        if (_monitoring) return Task.CompletedTask;

        _lastSequence = unchecked((int)GetClipboardSequenceNumber());
        _monitoring = true;

        // Original checks NSPasteboard every 0.5 s; 250 ms keeps the notch
        // responsive without measurable CPU cost (only a sequence number is
        // read until the clipboard actually changes).
        _pollTimer.Change(0, 250);
        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync(CancellationToken ct = default)
    {
        if (!_monitoring) return Task.CompletedTask;

        _monitoring = false;
        _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _monitoring = false;
        _pollTimer.Dispose();
        _requests.CompleteAdding();
        if (_clipboardThread.IsAlive)
            _clipboardThread.Join(TimeSpan.FromSeconds(2));

        SaveHistory();
    }

    // ------------------------------------------------------------------
    // STA worker + polling
    // ------------------------------------------------------------------

    private void ClipboardThreadProc()
    {
        foreach (var request in _requests.GetConsumingEnumerable())
        {
            try
            {
                request();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] clipboard operation failed: {ex.Message}");
            }
        }
    }

    private Task RunOnClipboardThread(Action action, CancellationToken ct)
    {
        if (_disposed) return Task.CompletedTask;

        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _requests.Add(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            completion.TrySetCanceled();
        }

        return completion.Task.WaitAsync(ct);
    }

    private void OnPoll(object? state)
    {
        if (!_monitoring) return;

        var sequence = unchecked((int)GetClipboardSequenceNumber());
        if (sequence == Volatile.Read(ref _lastSequence)) return;
        Volatile.Write(ref _lastSequence, sequence);

        // Collapse a burst of changes into one pending clipboard read.
        if (Interlocked.Exchange(ref _captureQueued, 1) == 1) return;

        try
        {
            _requests.Add(() =>
            {
                try
                {
                    CaptureAndAdd();
                }
                finally
                {
                    Volatile.Write(ref _captureQueued, 0);
                }
            });
        }
        catch (InvalidOperationException)
        {
            Volatile.Write(ref _captureQueued, 0);
        }
    }

    // ------------------------------------------------------------------
    // Capture
    // ------------------------------------------------------------------

    private void CaptureAndAdd()
    {
        var item = CaptureClipboardWithRetry();
        if (item is not null)
            AddItemInternal(item);
    }

    private ClipboardItem? CaptureClipboardWithRetry()
    {
        const int clipboardBusy = unchecked((int)0x800401D0);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return CaptureClipboard();
            }
            catch (COMException ex) when (ex.HResult == clipboardBusy)
            {
                Thread.Sleep(20);
            }
            catch (ExternalException)
            {
                Thread.Sleep(20);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] clipboard read failed: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    private ClipboardItem? CaptureClipboard()
    {
        var data = WpfClipboard.GetDataObject();
        if (data is null) return null;

        // Priority 1: files. Image files opened from Explorer are treated as
        // images (the original ClipboardManager does the same), otherwise the
        // entry is a file list.
        if (data.GetDataPresent(DataFormats.FileDrop) || WpfClipboard.ContainsFileDropList())
        {
            var files = GetFileDropList();
            if (files.Count > 0)
            {
                var imageFile = files.FirstOrDefault(IsImagePath);
                if (imageFile is not null)
                {
                    try
                    {
                        return CreateImageItem(File.ReadAllBytes(imageFile));
                    }
                    catch
                    {
                        // Fall through to a file entry if the image cannot be read.
                    }
                }

                return new ClipboardItem
                {
                    Type = ClipboardItemType.File,
                    FilePaths = files,
                };
            }
        }

        // Priority 2: raw image data (screenshots, copied pixels).
        if (WpfClipboard.ContainsImage())
        {
            var image = WpfClipboard.GetImage();
            if (image is not null)
            {
                var bytes = EncodePng(image);
                if (bytes.Length > 0)
                    return CreateImageItem(bytes);
            }
        }

        string? text = null;
        if (data.GetDataPresent(DataFormats.UnicodeText))
            text = data.GetData(DataFormats.UnicodeText) as string;
        if (string.IsNullOrEmpty(text) && data.GetDataPresent(DataFormats.Text))
            text = data.GetData(DataFormats.Text) as string;

        var rtf = data.GetDataPresent(DataFormats.Rtf)
            ? data.GetData(DataFormats.Rtf) as string
            : null;
        var html = data.GetDataPresent(DataFormats.Html)
            ? data.GetData(DataFormats.Html) as string
            : null;

        // Priority 3/4: rich text wins over its plain-text representation so
        // formatting is not lost at capture time.
        if (!string.IsNullOrEmpty(rtf) || !string.IsNullOrEmpty(html))
        {
            return new ClipboardItem
            {
                Type = ClipboardItemType.Rtf,
                Text = text,
                Rtf = rtf,
                Html = html,
            };
        }

        // Priority 5: plain-text URLs (an address-bar copy has no rich data).
        if (!string.IsNullOrEmpty(text) &&
            (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return new ClipboardItem { Type = ClipboardItemType.Url, Text = text };
        }

        // Priority 6: plain text.
        if (!string.IsNullOrEmpty(text))
            return new ClipboardItem { Type = ClipboardItemType.Text, Text = text };

        return null;
    }

    private static List<string> GetFileDropList()
    {
        var result = new List<string>();
        try
        {
            var list = WpfClipboard.GetFileDropList();
            foreach (var path in list)
            {
                if (!string.IsNullOrEmpty(path))
                    result.Add(path);
            }
        }
        catch
        {
            // Clipboard changed between checks; the next poll retries.
        }

        return result;
    }

    private static bool IsImagePath(string path)
        => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static ClipboardItem CreateImageItem(byte[] bytes) => new()
    {
        Type = ClipboardItemType.Image,
        ImageBytes = bytes,
    };

    // ------------------------------------------------------------------
    // Copy back
    // ------------------------------------------------------------------

    private static DataObject BuildDataObject(ClipboardItem item)
    {
        var data = new DataObject();

        switch (item.Type)
        {
            case ClipboardItemType.Image:
                if (item.ImageBytes is { Length: > 0 } bytes)
                    data.SetImage(DecodeBitmap(bytes));
                break;

            case ClipboardItemType.File:
                var files = item.FilePaths?.Where(File.Exists).ToArray();
                if (files is { Length: > 0 })
                {
                    var list = new StringCollection();
                    list.AddRange(files);
                    data.SetFileDropList(list);
                }
                break;

            case ClipboardItemType.Rtf:
                if (!string.IsNullOrEmpty(item.Rtf))
                    data.SetData(DataFormats.Rtf, item.Rtf);
                if (!string.IsNullOrEmpty(item.Html))
                    data.SetData(DataFormats.Html, item.Html);
                if (!string.IsNullOrEmpty(item.Text))
                    data.SetText(item.Text);
                break;

            default:
                if (!string.IsNullOrEmpty(item.Text))
                    data.SetText(item.Text);
                break;
        }

        if (data.GetFormats().Length == 0 && !string.IsNullOrEmpty(item.Preview))
            data.SetText(item.Preview);

        return data;
    }

    private static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapImage DecodeBitmap(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = new MemoryStream(bytes);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    // ------------------------------------------------------------------
    // History mutation
    // ------------------------------------------------------------------

    private void AddItemInternal(ClipboardItem raw)
    {
        var item = Normalize(raw);
        List<ClipboardItem> removed;

        lock (_sync)
        {
            // The original skips an item that is already anywhere in history
            // instead of moving a duplicate to the top.
            if (_history.Any(existing => IsSameContent(existing, item)))
                return;

            PrepareImageLocked(item);
            _history.Insert(0, item);
            removed = TrimLocked();
            PersistStateLocked();
        }

        DeleteImageFiles(removed);
        ItemAdded?.Invoke(this, item);
    }

    private static ClipboardItem Normalize(ClipboardItem item)
    {
        var type = item.Type;
        if (type == ClipboardItemType.Unknown)
        {
            if (item.ImageBytes is { Length: > 0 } || !string.IsNullOrEmpty(item.ImageFileName))
                type = ClipboardItemType.Image;
            else if (item.FilePaths?.Any() == true)
                type = ClipboardItemType.File;
            else if (!string.IsNullOrEmpty(item.Rtf) || !string.IsNullOrEmpty(item.Html))
                type = ClipboardItemType.Rtf;
            else if (!string.IsNullOrEmpty(item.Text) &&
                     (item.Text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      item.Text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                type = ClipboardItemType.Url;
            else if (!string.IsNullOrEmpty(item.Text))
                type = ClipboardItemType.Text;
        }

        if (type == item.Type) return item;

        return new ClipboardItem
        {
            Id = item.Id,
            Timestamp = item.Timestamp,
            Type = type,
            Text = item.Text,
            Html = item.Html,
            Rtf = item.Rtf,
            FilePaths = item.FilePaths,
            ImageBytes = item.ImageBytes,
            ImageFileName = item.ImageFileName,
            SourceApp = item.SourceApp,
        };
    }

    private void PrepareImageLocked(ClipboardItem item)
    {
        if (item.Type != ClipboardItemType.Image || item.ImageBytes is not { Length: > 0 })
            return;

        if (!_persistHistory)
        {
            _inMemoryImages[item.Id] = item.ImageBytes;
            return;
        }

        if (string.IsNullOrEmpty(item.ImageFileName))
        {
            item.ImageFileName = $"clipboard_image_{Guid.NewGuid():N}.png";
            try
            {
                File.WriteAllBytes(Path.Combine(_dataDirectory, item.ImageFileName), item.ImageBytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] clipboard image save failed: {ex.Message}");
                item.ImageFileName = null;
                _inMemoryImages[item.Id] = item.ImageBytes;
            }
        }
    }

    private List<ClipboardItem> TrimLocked()
    {
        var removed = new List<ClipboardItem>();
        while (_history.Count > _maxHistorySize)
        {
            var last = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            removed.Add(last);
        }

        return removed;
    }

    private static bool IsSameContent(ClipboardItem a, ClipboardItem b)
    {
        if (a.Type != b.Type) return false;

        return a.Type switch
        {
            ClipboardItemType.Text or ClipboardItemType.Url or ClipboardItemType.Unknown =>
                string.Equals(a.Text, b.Text, StringComparison.Ordinal),
            ClipboardItemType.Image => ImagesEqual(a, b),
            ClipboardItemType.File => FileListsEqual(a, b),
            ClipboardItemType.Rtf =>
                string.Equals(a.Text, b.Text, StringComparison.Ordinal) &&
                string.Equals(a.Rtf, b.Rtf, StringComparison.Ordinal) &&
                string.Equals(a.Html, b.Html, StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool ImagesEqual(ClipboardItem a, ClipboardItem b)
    {
        if (a.ImageBytes is not null && b.ImageBytes is not null)
            return a.ImageBytes.AsSpan().SequenceEqual(b.ImageBytes);

        return !string.IsNullOrEmpty(a.ImageFileName) &&
               string.Equals(a.ImageFileName, b.ImageFileName, StringComparison.Ordinal);
    }

    private static bool FileListsEqual(ClipboardItem a, ClipboardItem b)
    {
        var left = a.FilePaths?.ToArray() ?? Array.Empty<string>();
        var right = b.FilePaths?.ToArray() ?? Array.Empty<string>();
        return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }

    private void DeleteImageFiles(IEnumerable<ClipboardItem> items)
    {
        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.ImageFileName))
            {
                try
                {
                    File.Delete(Path.Combine(_dataDirectory, item.ImageFileName));
                }
                catch
                {
                    // Best effort: cleanup runs again on next launch.
                }
            }

            lock (_sync)
            {
                _inMemoryImages.Remove(item.Id);
            }
        }
    }

    // ------------------------------------------------------------------
    // Persistence
    // ------------------------------------------------------------------

    private void PersistStateLocked()
    {
        if (!_persistHistory)
        {
            TryDeleteHistoryFileLocked();
            return;
        }

        PersistImagesLocked();
        WriteHistoryLocked();
    }

    private void PersistImagesLocked()
    {
        foreach (var item in _history)
        {
            if (item.Type != ClipboardItemType.Image ||
                item.ImageBytes is not { Length: > 0 } bytes)
                continue;

            if (string.IsNullOrEmpty(item.ImageFileName))
            {
                item.ImageFileName = $"clipboard_image_{Guid.NewGuid():N}.png";
            }

            var path = Path.Combine(_dataDirectory, item.ImageFileName);
            try
            {
                if (!File.Exists(path))
                    File.WriteAllBytes(path, bytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] clipboard image save failed: {ex.Message}");
            }
        }
    }

    private void RetainImagesLocked()
    {
        foreach (var item in _history)
        {
            if (item.Type != ClipboardItemType.Image) continue;

            if (item.ImageBytes is null && !string.IsNullOrEmpty(item.ImageFileName))
            {
                try
                {
                    var path = Path.Combine(_dataDirectory, item.ImageFileName);
                    if (File.Exists(path))
                        item.ImageBytes = File.ReadAllBytes(path);
                }
                catch
                {
                    // A missing image leaves the entry visible without a thumbnail.
                }
            }

            if (item.ImageBytes is not null)
                _inMemoryImages[item.Id] = item.ImageBytes;

            if (!string.IsNullOrEmpty(item.ImageFileName))
            {
                try
                {
                    File.Delete(Path.Combine(_dataDirectory, item.ImageFileName));
                }
                catch
                {
                    // Best effort.
                }
                item.ImageFileName = null;
            }
        }
    }

    private void WriteHistoryLocked()
    {
        try
        {
            var json = JsonSerializer.Serialize(_history, _jsonOptions);
            File.WriteAllText(_historyFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] clipboard history save failed: {ex.Message}");
        }
    }

    private void SaveHistory()
    {
        lock (_sync)
        {
            PersistStateLocked();
        }
    }

    private void TryDeleteHistoryFileLocked()
    {
        try
        {
            if (File.Exists(_historyFile))
                File.Delete(_historyFile);
        }
        catch
        {
            // Best effort.
        }
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(_historyFile)) return;

            var json = File.ReadAllText(_historyFile);
            var items = JsonSerializer.Deserialize<List<ClipboardItem>>(json, _jsonOptions);
            if (items is null) return;

            lock (_sync)
            {
                _history.Clear();
                foreach (var item in items)
                {
                    var normalized = Normalize(item);

                    if (normalized.Type == ClipboardItemType.Image &&
                        string.IsNullOrEmpty(normalized.ImageFileName) &&
                        normalized.ImageBytes is null)
                    {
                        // Nothing backed this image; keep the row out of history.
                        continue;
                    }

                    if (normalized.Type == ClipboardItemType.Image &&
                        !string.IsNullOrEmpty(normalized.ImageFileName))
                    {
                        try
                        {
                            var path = Path.Combine(_dataDirectory, normalized.ImageFileName);
                            if (File.Exists(path))
                                normalized.ImageBytes = File.ReadAllBytes(path);
                        }
                        catch
                        {
                            // Keep the item; the thumbnail is simply unavailable.
                        }
                    }

                    _history.Add(normalized);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] clipboard history load failed: {ex.Message}");
        }
    }

    private void CleanupOrphanedImageFiles()
    {
        try
        {
            HashSet<string> referenced;
            lock (_sync)
            {
                referenced = _history
                    .Select(i => i.ImageFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var file in Directory.EnumerateFiles(_dataDirectory))
            {
                var name = Path.GetFileName(file);
                if (!referenced.Contains(name))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch
        {
            // Best effort.
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
