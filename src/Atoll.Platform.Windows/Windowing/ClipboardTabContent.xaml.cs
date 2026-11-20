using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Atoll.Core.Contracts;
using Atoll.Core.ViewModels;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Clipboard history tab for the notch. Translates the original
/// <c>NotchClipboardView</c>: card grid, empty state, clear-history
/// confirmation and drag-out. DataContext is the ShellViewModel.
/// </summary>
public partial class ClipboardTabContent : UserControl
{
    private ShellViewModel? _shell;
    private IClipboardService? _clipboard;
    private DispatcherTimer? _timeAgoTimer;
    private bool _subscribed;

    public ClipboardTabContent()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _shell = DataContext as ShellViewModel;
        _clipboard = _shell?.ClipboardService;
        if (_shell is null || _clipboard is null) return;

        if (!_subscribed)
        {
            _clipboard.ItemAdded += OnHistoryChanged;
            _clipboard.ItemRemoved += OnHistoryChanged;
            _clipboard.Cleared += OnHistoryCleared;
            _subscribed = true;
        }

        // The original starts ClipboardManager when the view appears.
        if (_shell.GetSetting("enableClipboardManager", true) && !_clipboard.IsMonitoring)
            _ = _clipboard.StartMonitoringAsync();

        _timeAgoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timeAgoTimer.Tick += (_, _) => UpdateRelativeTimes();
        _timeAgoTimer.Start();

        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timeAgoTimer?.Stop();
        _timeAgoTimer = null;

        if (_clipboard is not null && _subscribed)
        {
            _clipboard.ItemAdded -= OnHistoryChanged;
            _clipboard.ItemRemoved -= OnHistoryChanged;
            _clipboard.Cleared -= OnHistoryCleared;
            _subscribed = false;
        }
    }

    private void OnHistoryChanged(object? sender, ClipboardItem item) => RefreshSoon();

    private void OnHistoryCleared(object? sender, ClipboardClearedEventArgs e) => RefreshSoon();

    private void RefreshSoon()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(Refresh));
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (_clipboard is null) return;

        var items = _clipboard.History;
        CardsPanel.Children.Clear();

        foreach (var item in items)
        {
            var card = new ClipboardCardControl
            {
                Margin = new Thickness(0, 0, 8, 8),
                VerticalAlignment = VerticalAlignment.Top,
            };
            card.Bind(item, _clipboard);
            card.DeleteRequested += OnDeleteRequested;
            card.DragStateChanged += OnDragStateChanged;
            CardsPanel.Children.Add(card);
        }

        var empty = items.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        HistoryScroller.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        if (empty)
            ClearOverlay.Visibility = Visibility.Collapsed;
    }

    private void UpdateRelativeTimes()
    {
        foreach (var child in CardsPanel.Children)
        {
            if (child is ClipboardCardControl card)
                card.UpdateTimeAgo();
        }
    }

    private void OnDeleteRequested(object? sender, ClipboardItem item)
    {
        if (_clipboard is null) return;
        _ = _clipboard.RemoveItemAsync(item.Id);
    }

    private void OnDragStateChanged(object? sender, bool dragging)
    {
        if (_shell is null) return;

        if (dragging) _shell.BeginClipboardDrag();
        else _shell.EndClipboardDrag();
    }

    private void OnRequestClear(object sender, RoutedEventArgs e)
    {
        if (_clipboard is null || _clipboard.History.Count == 0) return;

        _shell?.CancelNotchAutoClose();
        ClearOverlay.Visibility = Visibility.Visible;
    }

    private void OnCancelClear(object sender, RoutedEventArgs e)
    {
        ClearOverlay.Visibility = Visibility.Collapsed;
    }

    private async void OnConfirmClear(object sender, RoutedEventArgs e)
    {
        ClearOverlay.Visibility = Visibility.Collapsed;
        if (_clipboard is not null)
            await _clipboard.ClearHistoryAsync();
    }
}
