using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Core.Design;
using Atoll.Core.Models;
using Atoll.Core.ViewModels;
using Atoll.Platform.Windows.Interop;

namespace Atoll.Platform.Windows.Windowing;

/// <summary>
/// Manages one floating notch window per display.
/// Translates macOS <c>DynamicIslandApp</c> window creation/positioning
/// (<c>createDynamicIslandWindow</c>, <c>positionWindow</c>,
/// <c>ensureWindowSize</c>, <c>hideWindowsForLock</c>,
/// <c>restoreWindowsAfterLock</c>) to Windows.
/// </summary>
public sealed class WindowService : IWindowService
{
    private readonly ILogger _logger;
    private readonly ISettingsService? _settings;
    private readonly Dictionary<string, NotchWindow> _windowsByDisplay = new();
    private readonly Dictionary<string, NotchViewModel> _viewModelsByDisplay = new();
    private readonly object _lock = new();

    public event EventHandler<DisplayChangedEventArgs>? DisplayConfigurationChanged;
    public event EventHandler<WindowStateChangedEventArgs>? NotchWindowStateChanged;

    public IReadOnlyList<DisplayInfo> Displays { get; private set; } =
        Array.Empty<DisplayInfo>();

    /// <summary>
    /// Set after construction to break the circular dependency:
    /// ShellViewModel needs IWindowService; WindowService needs ShellViewModel
    /// for tab content rendering. Assigning the shell creates the per-display
    /// windows and publishes the display list so the shell can track notches.
    /// </summary>
    public ShellViewModel? Shell
    {
        get => _shell;
        set
        {
            _shell = value;
            if (value is null) return;

            EnsureWindowsForDisplays();
            DisplayConfigurationChanged?.Invoke(this,
                new DisplayChangedEventArgs { Displays = Displays });
        }
    }
    private ShellViewModel? _shell;

    public WindowService(ILogger logger, ISettingsService? settings = null)
    {
        _logger = logger;
        _settings = settings;
        if (_settings is not null)
            _settings.SettingChanged += OnSettingChanged;
        RefreshDisplays();
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        switch (e.Key)
        {
            case "openNotchWidth":
            case "closedNotchWidth":
            case "notchHeight":
            case "nonNotchHeight":
                lock (_lock)
                {
                    foreach (var vm in _viewModelsByDisplay.Values)
                        ApplySizingSettings(vm);
                    foreach (var window in _windowsByDisplay.Values)
                        window.Dispatcher?.Invoke(window.PositionOnScreen);
                }
                break;

            case "enableShadow":
                var shadow = e.NewValue is bool sv ? sv : true;
                ForEachWindow(w => w.SetShadowEnabled(shadow));
                break;

            case "minimumHoverDuration":
                var seconds = e.NewValue is null ? 0.3 : Convert.ToDouble(e.NewValue);
                ForEachWindow(w => w.SetHoverDuration(seconds));
                break;

            // Feature toggles that add/remove a notch tab: rebuild the tab bar.
            case "showCalendar":
            case "enableStatsFeature":
            case "enableTimerFeature":
            case "enableTerminalFeature":
            case "enableClipboardManager":
                ForEachWindow(w => w.RefreshContent());
                break;
        }
    }

    private void ForEachWindow(Action<NotchWindow> action)
    {
        lock (_lock)
        {
            foreach (var window in _windowsByDisplay.Values)
                window.Dispatcher?.Invoke(() => action(window));
        }
    }

    private void ApplySizingSettings(NotchViewModel vm)
    {
        if (_settings is null) return;

        vm.ClosedWidth = _settings.Get("closedNotchWidth",
            vm.ClosedWidth > 0 ? vm.ClosedWidth : DesignTokens.ClosedNotchWidth);
        vm.OpenWidth = _settings.Get("openNotchWidth",
            vm.OpenWidth > 0 ? vm.OpenWidth : DesignTokens.OpenNotchWidth);
        vm.ClosedHeight = _settings.Get("nonNotchHeight", DesignTokens.NonNotchHeight);
        vm.OpenHeight = DesignTokens.OpenNotchHeight;
    }

    public void RefreshDisplays()
    {
        var displays = MonitorHelper.EnumerateDisplays();
        lock (_lock)
        {
            Displays = displays;
        }

        // Create windows/VMs first so the shell can resolve them when it
        // handles DisplayConfigurationChanged (hotplug ordering).
        EnsureWindowsForDisplays();
        DisplayConfigurationChanged?.Invoke(this, new DisplayChangedEventArgs { Displays = displays });
    }

    private void EnsureWindowsForDisplays()
    {
        if (Shell is null) return; // windows are created when the shell is wired

        lock (_lock)
        {
            foreach (var display in Displays)
            {
                if (_windowsByDisplay.ContainsKey(display.Id)) continue;

                var vm = new NotchViewModel();
                ApplySizingSettings(vm);
                _viewModelsByDisplay[display.Id] = vm;

                var window = new NotchWindow(vm, Shell, display, _logger);
                _windowsByDisplay[display.Id] = window;

                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(NotchViewModel.State))
                        NotchWindowStateChanged?.Invoke(this, new WindowStateChangedEventArgs
                        {
                            DisplayId = display.Id,
                            State = vm.State
                        });
                };

                // Show the window but do not activate (stays behind focus).
                window.Show();
                _logger.LogInfo($"Notch window created for display {display.Id}");
            }
        }
    }

    public Task SetNotchVisibleAsync(string displayId, bool visible, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_windowsByDisplay.TryGetValue(displayId, out var window))
            {
                if (visible) window.Show();
                else window.Hide();
            }
        }
        return Task.CompletedTask;
    }

    public Task SetNotchStateAsync(string displayId, NotchState state, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_viewModelsByDisplay.TryGetValue(displayId, out var vm))
                vm.State = state;
        }
        return Task.CompletedTask;
    }

    public Task PositionNotchAsync(string displayId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_windowsByDisplay.TryGetValue(displayId, out var window))
                window.Dispatcher?.Invoke(window.PositionOnScreen);
        }
        return Task.CompletedTask;
    }

    public Task HideAllAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            foreach (var window in _windowsByDisplay.Values)
                window.Dispatcher?.Invoke(() => window.Hide());
        }
        return Task.CompletedTask;
    }

    public Task RestoreAllAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            foreach (var window in _windowsByDisplay.Values)
                window.Dispatcher?.Invoke(() => window.Show());
        }
        return Task.CompletedTask;
    }

    /// <summary>Get the view model for a display's notch (for UI content binding).</summary>
    public NotchViewModel? GetViewModel(string displayId)
    {
        lock (_lock)
        {
            return _viewModelsByDisplay.TryGetValue(displayId, out var vm) ? vm : null;
        }
    }

    public void Dispose()
    {
        if (_settings is not null)
            _settings.SettingChanged -= OnSettingChanged;

        lock (_lock)
        {
            foreach (var window in _windowsByDisplay.Values)
                window.Close();
            _windowsByDisplay.Clear();
            _viewModelsByDisplay.Clear();
        }
    }
}
