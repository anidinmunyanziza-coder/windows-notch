using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Atoll.Core.Contracts;
using Atoll.Core.ViewModels;
using DataTransferManager = Windows.ApplicationModel.DataTransfer.DataTransferManager;
using DataRequestedEventArgs = Windows.ApplicationModel.DataTransfer.DataRequestedEventArgs;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// File shelf tab — drop files onto it to keep them handy, then drag them back
/// out to any app. Mirrors the original Shelf / drop.swift. Items are references
/// to the files on disk, not copies.
/// </summary>
public partial class ShelfTabContent : UserControl
{
    private ShellViewModel? Shell => DataContext as ShellViewModel;
    private IShelfService? Service => Shell?.ShelfService;

    private Point _dragStart;
    private ShelfItem? _dragCandidate;

    public ShelfTabContent()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Service is { } s)
            {
                s.ItemAdded += OnItemsChanged;
                s.ItemRemoved += OnItemsChanged;
                s.Cleared += OnCleared;
            }
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            if (Service is { } s)
            {
                s.ItemAdded -= OnItemsChanged;
                s.ItemRemoved -= OnItemsChanged;
                s.Cleared -= OnCleared;
            }
        };

        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    private void OnItemsChanged(object? sender, ShelfItem e) => Dispatcher.Invoke(Refresh);
    private void OnCleared(object? sender, EventArgs e) => Dispatcher.Invoke(Refresh);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (Service is null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        e.Handled = true;

        var downloads = DownloadsFolder();
        foreach (var p in paths)
        {
            // Copy the dropped file into Downloads, then shelve the copy.
            var shelved = await Task.Run(() => CopyIntoDownloads(p, downloads));
            if (shelved is not null)
                await Service.AddItemAsync(shelved);
        }
    }

    private static string DownloadsFolder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloads = Path.Combine(profile, "Downloads");
        try { Directory.CreateDirectory(downloads); } catch { }
        return downloads;
    }

    private static string? CopyIntoDownloads(string source, string downloads)
    {
        try
        {
            if (Directory.Exists(source)) return source; // folders are shelved by reference
            if (!File.Exists(source)) return null;

            var name = Path.GetFileName(source);
            var dest = Path.Combine(downloads, name);
            if (string.Equals(Path.GetDirectoryName(source), downloads, StringComparison.OrdinalIgnoreCase))
                return source; // already in Downloads

            // Avoid clobbering an existing file: name, name (1), name (2), …
            var baseName = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            var n = 1;
            while (File.Exists(dest))
                dest = Path.Combine(downloads, $"{baseName} ({n++}){ext}");

            File.Copy(source, dest);
            return dest;
        }
        catch
        {
            return null;
        }
    }

    private void Refresh()
    {
        ItemsPanel.Children.Clear();
        var items = Service?.Items ?? Array.Empty<ShelfItem>();
        foreach (var item in items)
            ItemsPanel.Children.Add(CreateCard(item));
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement CreateCard(ShelfItem item)
    {
        var glyph = new TextBlock
        {
            Text = GlyphFor(item.FilePath),
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 26,
            Foreground = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var name = new TextBlock
        {
            Text = item.DisplayName ?? Path.GetFileName(item.FilePath),
            FontSize = 10.5,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 84,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 0),
        };

        var remove = new Button
        {
            Content = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(0),
            Width = 18, Height = 18,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -2, -2, 0),
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
        };
        remove.Click += async (_, _) => { if (Service is { } s) await s.RemoveItemAsync(item.Id); };

        var inner = new StackPanel();
        inner.Children.Add(glyph);
        inner.Children.Add(name);

        var content = new Grid();
        content.Children.Add(inner);
        content.Children.Add(remove);

        var card = new Border
        {
            Width = 100,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 10, 8, 10),
            Margin = new Thickness(5),
            Cursor = Cursors.Hand,
            ToolTip = item.FilePath,
            Child = content,
        };

        // Hover reveals the remove button.
        card.MouseEnter += (_, _) => remove.Visibility = Visibility.Visible;
        card.MouseLeave += (_, _) => remove.Visibility = Visibility.Collapsed;

        // Drag the file back out.
        card.PreviewMouseLeftButtonDown += (_, e) => { _dragStart = e.GetPosition(this); _dragCandidate = item; };
        card.MouseMove += OnCardMouseMove;

        return card;
    }

    private void OnCardMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null) return;
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var path = _dragCandidate.FilePath;
        _dragCandidate = null;
        if (!File.Exists(path) && !Directory.Exists(path)) return;

        // Keep the notch open for the duration of the drag.
        Shell?.BeginClipboardDrag();
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { path });
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
        finally
        {
            Shell?.EndClipboardDrag();
        }
    }

    private async void OnClear(object sender, RoutedEventArgs e)
    {
        if (Service is { } s) await s.ClearAsync();
    }

    // ------------------------------------------------------------------
    // AirDrop → Windows share sheet (Nearby Share)
    // ------------------------------------------------------------------

    [ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
        void ShowShareUIForWindow(IntPtr appWindow);
    }

    private static readonly Guid DataTransferManagerIid =
        new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);

    private static DataTransferManager? _shareManager;
    private static IReadOnlyList<string> _pendingShare = Array.Empty<string>();

    /// <summary>Shares the shelf's files through the system share sheet — on
    /// Windows that's where Nearby Share (the AirDrop equivalent) lives.</summary>
    private void OnAirDrop(object sender, RoutedEventArgs e)
    {
        var paths = (Service?.Items ?? Array.Empty<ShelfItem>())
            .Select(i => i.FilePath)
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .ToList();
        if (paths.Count == 0) return;

        try
        {
            var hwnd = new WindowInteropHelper(Window.GetWindow(this)!).Handle;
            var interop = DataTransferManager.As<IDataTransferManagerInterop>();
            var iid = DataTransferManagerIid;

            if (_shareManager is null)
            {
                var ptr = interop.GetForWindow(hwnd, ref iid);
                _shareManager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr);
                _shareManager.DataRequested += OnShareDataRequested;
            }

            _pendingShare = paths;
            interop.ShowShareUIForWindow(hwnd);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[shelf] share sheet unavailable: {ex.Message}");
        }
    }

    private static async void OnShareDataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        var deferral = args.Request.GetDeferral();
        try
        {
            var items = new List<global::Windows.Storage.IStorageItem>();
            foreach (var path in _pendingShare)
            {
                if (File.Exists(path))
                    items.Add(await global::Windows.Storage.StorageFile.GetFileFromPathAsync(path));
                else if (Directory.Exists(path))
                    items.Add(await global::Windows.Storage.StorageFolder.GetFolderFromPathAsync(path));
            }

            args.Request.Data.Properties.Title = items.Count == 1 ? items[0].Name : $"{items.Count} items";
            args.Request.Data.SetStorageItems(items);
        }
        catch (Exception ex)
        {
            args.Request.FailWithDisplayText("Couldn't share these files: " + ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private static string GlyphFor(string path)
    {
        if (Directory.Exists(path)) return ""; // folder
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" => "", // image
            ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" => "",            // video
            ".mp3" or ".wav" or ".flac" or ".m4a" => "",                        // audio
            ".pdf" or ".doc" or ".docx" or ".txt" or ".md" => "",              // document
            ".zip" or ".rar" or ".7z" => "",                                    // archive
            _ => "",                                                            // generic file
        };
    }
}
