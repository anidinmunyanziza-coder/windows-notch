using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Atoll.Core.Contracts;
using Atoll.Core.Media;
using Atoll.Core.Models;
using Atoll.Core.Usage;

namespace Atoll.Core.ViewModels;

/// <summary>
/// Central application state. Mirrors <c>DynamicIslandViewCoordinator</c>
/// (tab order, currentView) plus per-display notch state.
/// All settings flow through <c>ISettingsService</c> (374-key schema).
/// </summary>
public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settings;
    private readonly IWindowService _windowService;
    private readonly IMediaService _mediaService;
    private readonly ISystemMetricsService _metricsService;
    private readonly ITimerService _timerService;
    private readonly IClipboardService _clipboardService;
    private readonly IShelfService _shelfService;
    private readonly ILogger _logger;

    // Tab order mirrors DynamicIslandViewCoordinator.tabOrder.
    private static readonly NotchView[] _tabOrder =
    {
        NotchView.Home, NotchView.Shelf, NotchView.Timer, NotchView.Stats,
        NotchView.LlmUsage, NotchView.ColorPicker, NotchView.Notes,
        NotchView.Clipboard, NotchView.ExtensionExperience
    };

    private readonly ObservableCollection<NotchViewModel> _notches = new();
    public IReadOnlyList<NotchViewModel> Notches => _notches;

    public ObservableCollection<NotchView> Tabs { get; } = new();

    private NotchView _currentView = NotchView.Home;
    public NotchView CurrentView
    {
        get => _currentView;
        set
        {
            if (SetField(ref _currentView, value))
            {
                var newIdx = Array.IndexOf(_tabOrder, _currentView);
                TabSwitchForward = newIdx >= 0;
                OnPropertyChanged(nameof(CurrentView));
            }
        }
    }

    private bool _tabSwitchForward = true;
    public bool TabSwitchForward
    {
        get => _tabSwitchForward;
        set => SetField(ref _tabSwitchForward, value);
    }

    private MediaPlaybackState? _mediaState;
    public MediaPlaybackState? MediaState
    {
        get => _mediaState;
        private set => SetField(ref _mediaState, value);
    }

    private SystemMetricsSnapshot? _metricsSnapshot;
    public SystemMetricsSnapshot? MetricsSnapshot
    {
        get => _metricsSnapshot;
        private set => SetField(ref _metricsSnapshot, value);
    }

    private TimerState? _timerState;
    public TimerState? TimerState
    {
        get => _timerState;
        private set => SetField(ref _timerState, value);
    }

    public ITimerService TimerService => _timerService;

    public IClipboardService ClipboardService => _clipboardService;

    public IShelfService ShelfService => _shelfService;

    private readonly INotesService _notesService;
    public INotesService NotesService => _notesService;

    private readonly ILlmUsageService _llmUsage;
    public ILlmUsageService LlmUsage => _llmUsage;

    // ── Lyrics (the original's MusicManager lyrics state) ──────────────────
    private readonly ILyricsService _lyricsService;
    private string _lyricsKey = "";
    private CancellationTokenSource? _lyricsCts;

    private LyricsResult _lyrics = LyricsResult.Unavailable;
    /// <summary>Lyrics for the current track (published from a worker thread).</summary>
    public LyricsResult Lyrics
    {
        get => _lyrics;
        private set => SetField(ref _lyrics, value);
    }

    /// <summary>Fetches lyrics when the track (title + artist) changes; stale
    /// lookups are cancelled. Gated by Defaults[.enableLyrics].</summary>
    private void UpdateLyricsFor(MediaPlaybackState state)
    {
        var title = state.Title ?? "";
        var artist = state.Artist ?? "";
        var key = title.Length == 0 ? "" : title + "\u0001" + artist;
        if (key == _lyricsKey) return;
        _lyricsKey = key;

        _lyricsCts?.Cancel();
        if (key.Length == 0 || state.IsAdvertisement || !GetSetting("enableLyrics", true))
        {
            Lyrics = LyricsResult.Unavailable;
            return;
        }

        Lyrics = LyricsResult.Loading;
        var cts = _lyricsCts = new CancellationTokenSource();
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var result = await _lyricsService.GetAsync(title, artist, state.Album ?? "", state.Duration, cts.Token);
                if (!cts.IsCancellationRequested && key == _lyricsKey) Lyrics = result;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogDebug($"Lyrics lookup failed: {ex.Message}"); }
        });
    }

    /// <summary>Re-evaluates lyrics after Defaults[.enableLyrics] changes.</summary>
    public void RefreshLyrics()
    {
        _lyricsKey = "";
        if (MediaState is { } m) UpdateLyricsFor(m);
    }

    private volatile bool _isClipboardDragInProgress;
    private Timer? _clipboardDragResetTimer;

    /// <summary>
    /// True while a clipboard card is being dragged out. Mirrors
    /// ClipboardManager.isDraggingItem: the notch must not auto-close mid-drag.
    /// A bounded reset releases the flag if a drop is never observed.
    /// </summary>
    public bool IsClipboardDragInProgress => _isClipboardDragInProgress;

    public void BeginClipboardDrag()
    {
        _isClipboardDragInProgress = true;
        _clipboardDragResetTimer ??= new Timer(
            _ => _isClipboardDragInProgress = false,
            null,
            Timeout.Infinite,
            Timeout.Infinite);
        _clipboardDragResetTimer.Change(TimeSpan.FromSeconds(8), Timeout.InfiniteTimeSpan);
    }

    public void EndClipboardDrag()
    {
        _isClipboardDragInProgress = false;
        _clipboardDragResetTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public ShellViewModel(ISettingsService settings, IWindowService windowService, IMediaService mediaService, ISystemMetricsService metricsService, ITimerService timerService, IClipboardService clipboardService, IShelfService shelfService, INotesService notesService, ILyricsService lyricsService, ILlmUsageService llmUsage, ILogger logger)
    {
        _settings = settings;
        _windowService = windowService;
        _mediaService = mediaService;
        _metricsService = metricsService;
        _timerService = timerService;
        _clipboardService = clipboardService;
        _shelfService = shelfService;
        _notesService = notesService;
        _lyricsService = lyricsService;
        _llmUsage = llmUsage;
        _logger = logger;

        RebuildTabs();

        // Subscribe to media state changes.
        _mediaService.StateChanged += (_, state) =>
        {
            MediaState = state;
            UpdateLyricsFor(state);
            _logger.LogDebug($"Media: {state.Title} - {state.Artist} | {(state.IsPlaying ? "playing" : "paused")}");
        };

        // Subscribe to system metrics.
        _metricsService.MetricsCollected += (_, snap) =>
        {
            MetricsSnapshot = snap;
        };

        // Subscribe to timer state changes.
        _timerService.StateChanged += (_, state) =>
        {
            TimerState = state;
        };

        _timerService.Tick += (_, remaining) =>
        {
            TimerState = _timerService.State;
        };

        // Subscribe to clipboard changes.
        _clipboardService.ItemAdded += (_, item) =>
        {
            _logger.LogDebug($"Clipboard: item added ({item.Text?.Length ?? 0} chars)");
        };

        _windowService.DisplayConfigurationChanged += (_, e) =>
        {
            var ids = new HashSet<string>(e.Displays.Select(d => d.Id));

            // Drop notches for displays that disappeared.
            for (var i = _notches.Count - 1; i >= 0; i--)
            {
                if (!ids.Contains(NotchTagMap.Get(_notches[i]) ?? string.Empty))
                    _notches.RemoveAt(i);
            }

            foreach (var d in e.Displays)
            {
                if (!_notches.Any(n => NotchTagMap.Get(n) == d.Id))
                {
                    var vm = _windowService.GetViewModel(d.Id);
                    if (vm is not null)
                    {
                        NotchTagMap.Set(vm, d.Id);
                        _notches.Add(vm);
                    }
                }
            }
        };

        _windowService.NotchWindowStateChanged += (_, e) =>
        {
            _logger.LogInfo($"Notch {e.DisplayId} state -> {e.State}");
        };
    }

    private void RebuildTabs()
    {
        Tabs.Clear();
        foreach (var tab in _tabOrder)
            Tabs.Add(tab);
    }

    public void SwitchTab(NotchView view)
    {
        CurrentView = view;
        foreach (var vm in _notches)
            vm.State = NotchState.Open;
    }

    /// <summary>Raised when the notch asks to open the Settings window; the app
    /// shell (which owns the UI windows) handles it.</summary>
    public event Action? OpenSettingsRequested;
    public void RequestOpenSettings() => OpenSettingsRequested?.Invoke();

    public Task SendMediaCommand(MediaCommand command) => _mediaService.SendCommandAsync(command);

    /// <summary>Seeks the current track (scrubber drag).</summary>
    public Task SeekMedia(TimeSpan position) => _mediaService.SeekAsync(position);

    /// <summary>
    /// Advances playback control to the next detected media session (the
    /// Windows analogue of the original's AirPlay/output picker). No-op when
    /// zero or one session is present.
    /// </summary>
    public async Task CycleMediaPlayerAsync()
    {
        var sessions = await _mediaService.GetSessionsAsync();
        if (sessions.Count < 2) return;

        var currentId = MediaState?.PlayerAppUserModelId;
        var index = 0;
        for (var i = 0; i < sessions.Count; i++)
        {
            if (string.Equals(sessions[i].AppUserModelId, currentId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        var next = sessions[(index + 1) % sessions.Count];
        if (next.AppUserModelId is { Length: > 0 } id)
            await _mediaService.SwitchPlayerAsync(id);
    }

    public void CloseAllNotches()
    {
        CancelNotchAutoClose();
        foreach (var vm in _notches)
            vm.State = NotchState.Closed;
    }

    /// <summary>
    /// Toggles the notch on one display (or every display when
    /// <paramref name="displayId"/> is null). Mirrors the
    /// <c>toggleNotchOpen</c> handler, including its 3-second auto-close
    /// after a shortcut-driven open.
    /// </summary>
    public void ToggleNotchOpen(string? displayId = null, TimeSpan? autoCloseAfter = null)
    {
        var targets = (displayId is null
            ? _notches
            : _notches.Where(n => NotchTagMap.Get(n) == displayId)).ToList();

        if (targets.Count == 0) return;

        var anyOpen = targets.Any(n => n.State == NotchState.Open);
        foreach (var vm in targets)
            vm.State = anyOpen ? NotchState.Closed : NotchState.Open;

        if (anyOpen)
            CancelNotchAutoClose();
        else if (autoCloseAfter is { } delay)
            ScheduleNotchAutoClose(delay);
    }

    private System.Threading.Timer? _autoCloseTimer;

    /// <summary>
    /// Closes all notches after <paramref name="delay"/>. Used by the
    /// toggleNotchOpen shortcut, matching the original 3.0s work item.
    /// </summary>
    public void ScheduleNotchAutoClose(TimeSpan delay)
    {
        CancelNotchAutoClose();
        var context = SynchronizationContext.Current;
        _autoCloseTimer = new System.Threading.Timer(_ =>
        {
            if (context is not null)
                context.Post(__ => CloseAllNotches(), null);
            else
                CloseAllNotches();
        }, null, delay, Timeout.InfiniteTimeSpan);
    }

    public void CancelNotchAutoClose()
    {
        _autoCloseTimer?.Dispose();
        _autoCloseTimer = null;
    }

    public void ToggleNotch(string displayId)
    {
        var vm = _notches.FirstOrDefault(n => NotchTagMap.Get(n) == displayId);
        if (vm is null) return;
        vm.State = vm.State == NotchState.Open ? NotchState.Closed : NotchState.Open;
    }

    public T GetSetting<T>(string key, T defaultValue) => _settings.Get(key, defaultValue);
    public void SetSetting<T>(string key, T value) => _settings.Set(key, value);

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// Maps notch view models to their display ids.
public static class NotchTagMap
{
    private static readonly Dictionary<NotchViewModel, string> _map = new();
    public static void Set(NotchViewModel vm, string id) => _map[vm] = id;
    public static string? Get(NotchViewModel vm) => _map.TryGetValue(vm, out var id) ? id : null;
}
