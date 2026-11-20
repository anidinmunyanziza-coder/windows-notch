using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Atoll.Core.Contracts;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// One clipboard history card, matching the original
/// <c>ClipboardGridCard</c>: type icon/thumbnail, 50-character preview, type
/// and relative-time metadata, tap to copy, hover-revealed delete, and
/// drag-out for text, URLs, rich text, single files and images.
/// </summary>
public partial class ClipboardCardControl : UserControl
{
    private readonly DispatcherTimer _copiedTimer;
    private IClipboardService? _clipboard;
    private Point _pressPoint;
    private bool _dragStarted;
    private bool _suppressClick;

    public ClipboardCardControl()
    {
        InitializeComponent();

        _copiedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _copiedTimer.Tick += (_, _) =>
        {
            _copiedTimer.Stop();
            ResetCopiedFeedback();
        };
    }

    public ClipboardItem? Item { get; private set; }

    public event EventHandler<ClipboardItem>? DeleteRequested;

    /// <summary>Raised true when a drag-out starts and false when it ends.</summary>
    public event EventHandler<bool>? DragStateChanged;

    public void Bind(ClipboardItem item, IClipboardService clipboard)
    {
        Item = item;
        _clipboard = clipboard;

        PreviewText.Text = item.Preview;
        TypeText.Text = DisplayName(item.Type);
        IconGlyph.Text = Glyph(item.Type);
        UpdateTimeAgo();

        if (item.Type == ClipboardItemType.Image && item.ImageBytes is { Length: > 0 } bytes)
        {
            try
            {
                var bitmap = DecodeBitmap(bytes);
                ThumbnailHost.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                IconGlyph.Visibility = Visibility.Collapsed;
            }
            catch
            {
                // Leave the generic image glyph when the thumbnail cannot decode.
            }
        }

        AutomationProperties.SetName(this, $"{DisplayName(item.Type)}: {item.Preview}");
        AutomationProperties.SetHelpText(this,
            "Click to copy. Drag to another app. Delete removes this item.");
    }

    public void UpdateTimeAgo()
    {
        if (Item is null) return;

        var interval = DateTimeOffset.UtcNow - Item.Timestamp;
        if (interval.TotalSeconds < 60)
            TimeText.Text = "Just now";
        else if (interval.TotalMinutes < 60)
            TimeText.Text = $"{(int)interval.TotalMinutes}m";
        else
            TimeText.Text = $"{(int)interval.TotalHours}h";
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _pressPoint = e.GetPosition(this);
        _dragStarted = false;
        _suppressClick = false;
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (e.LeftButton != MouseButtonState.Pressed || _dragStarted || Item is null)
            return;

        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var data = BuildDragData();
        if (data is null) return;

        _dragStarted = true;
        DragStateChanged?.Invoke(this, true);
        try
        {
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        finally
        {
            DragStateChanged?.Invoke(this, false);
            _suppressClick = true;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_suppressClick)
        {
            _suppressClick = false;
            _dragStarted = false;
            return;
        }

        if (!_dragStarted)
            _ = CopyItemAsync();

        _dragStarted = false;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Item is not null)
            DeleteRequested?.Invoke(this, Item);
    }

    private async System.Threading.Tasks.Task CopyItemAsync()
    {
        if (_clipboard is null || Item is null) return;

        try
        {
            await _clipboard.CopyToClipboardAsync(Item);
            ShowCopiedFeedback();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] clipboard copy-back failed: {ex.Message}");
        }
    }

    private void ShowCopiedFeedback()
    {
        if (Item?.Type == ClipboardItemType.Image &&
            Item.ImageBytes is { Length: > 0 })
        {
            CopiedOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            IconGlyph.Text = "\uE73E";
            IconGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58));
        }

        _copiedTimer.Stop();
        _copiedTimer.Start();
    }

    private void ResetCopiedFeedback()
    {
        CopiedOverlay.Visibility = Visibility.Collapsed;
        if (Item is not null)
        {
            IconGlyph.Text = Glyph(Item.Type);
            IconGlyph.Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        }
    }

    private DataObject? BuildDragData()
    {
        if (Item is null) return null;

        // A single SwiftUI drag provider cannot carry more than one file; the
        // original leaves multi-file entries non-draggable for the same reason.
        if (Item.Type == ClipboardItemType.File && (Item.FilePaths?.Count() ?? 0) > 1)
            return null;

        var data = new DataObject();

        switch (Item.Type)
        {
            case ClipboardItemType.Image:
                if (Item.ImageBytes is not { Length: > 0 } bytes)
                    return null;

                try
                {
                    data.SetImage(DecodeBitmap(bytes));
                }
                catch
                {
                    // Still offer the PNG file below if the bitmap decodes.
                }

                try
                {
                    var tempDirectory = Path.Combine(Path.GetTempPath(), "AtollDrag");
                    Directory.CreateDirectory(tempDirectory);
                    var tempFile = Path.Combine(tempDirectory, $"{Item.Id}.png");
                    File.WriteAllBytes(tempFile, bytes);

                    var files = new StringCollection { tempFile };
                    data.SetFileDropList(files);
                }
                catch
                {
                    // The bitmap format above remains available.
                }
                break;

            case ClipboardItemType.File:
                var file = Item.FilePaths?.FirstOrDefault();
                if (file is not null && File.Exists(file))
                {
                    var files = new StringCollection { file };
                    data.SetFileDropList(files);
                }
                break;

            case ClipboardItemType.Rtf:
                if (!string.IsNullOrEmpty(Item.Rtf))
                    data.SetData(DataFormats.Rtf, Item.Rtf);
                if (!string.IsNullOrEmpty(Item.Html))
                    data.SetData(DataFormats.Html, Item.Html);
                if (!string.IsNullOrEmpty(Item.Text))
                    data.SetText(Item.Text);
                break;

            default:
                if (!string.IsNullOrEmpty(Item.Text))
                    data.SetText(Item.Text);
                break;
        }

        if (data.GetFormats().Length == 0 && !string.IsNullOrEmpty(Item.Preview))
            data.SetText(Item.Preview);

        return data;
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

    private static string DisplayName(ClipboardItemType type) => type switch
    {
        ClipboardItemType.Text => "Text",
        ClipboardItemType.Url => "URL",
        ClipboardItemType.File => "File",
        ClipboardItemType.Image => "Image",
        ClipboardItemType.Rtf => "Rich Text",
        _ => "Unknown",
    };

    private static string Glyph(ClipboardItemType type) => type switch
    {
        ClipboardItemType.Url => "\uE71B",   // Link
        ClipboardItemType.File => "\uE7C3",  // Page
        ClipboardItemType.Image => "\uEB9F", // Photo
        ClipboardItemType.Rtf => "\uE8A5",   // Document
        ClipboardItemType.Unknown => "\uE897",
        _ => "\uE8A5",                      // Document
    };
}
