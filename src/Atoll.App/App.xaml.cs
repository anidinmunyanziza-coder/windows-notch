using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Atoll.Core.Contracts;
using Atoll.Core.Infrastructure;
using Atoll.Core.Models;
using Atoll.Core.ViewModels;
using Atoll.Services.Media;
using Atoll.Services.Productivity;
using Atoll.Services.Productivity.Clipboard;
using Atoll.Services.Settings;
using Atoll.Services.SystemMonitoring;
using Atoll.Platform.Windows.Input;
using Atoll.Platform.Windows.Shell;
using Atoll.Platform.Windows.Windowing;
using Atoll.UI;
using Atoll.UI.Settings;

namespace Atoll.App;

public partial class App : Application
{
    private static readonly string _mutexName = "Local\\Ebullioscopic.Atoll.SingleInstance";
    private static Mutex? _singleInstanceMutex;
    private static SettingsWindow? _settingsWindow;

    private IHost? _host;

    public IHost? Host => _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Tray/notch app: closing a window must not terminate the process.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        bool createdNew;
        try
        {
            _singleInstanceMutex = new Mutex(true, _mutexName, out createdNew);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] could not create single-instance mutex: {ex.Message}");
            createdNew = true;
        }

        if (!createdNew)
        {
            Current.Shutdown();
            return;
        }

        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<ILogger>(sp => new ConsoleLogger(LogLevel.Info));
                services.AddSingleton<ISettingsService, JsonSettingsService>();
                services.AddSingleton<IMediaService, MediaService>();
                services.AddSingleton<ISystemMetricsService, SystemMetricsService>();
                services.AddSingleton<ITimerService, TimerService>();
                services.AddSingleton<IClipboardService, ClipboardService>();
                services.AddSingleton<IShelfService, Atoll.Services.Productivity.ShelfService>();
                services.AddSingleton<INotesService>(_ => new Atoll.Services.Productivity.NotesService());
                services.AddSingleton<Atoll.Core.Media.ILyricsService>(_ => new Atoll.Services.Media.LyricsService());
                services.AddSingleton<IAssistantService>(sp =>
                    new Atoll.Services.Assistant.OpenRouterAssistantService(sp.GetRequiredService<ISettingsService>()));
                services.AddSingleton<Atoll.Core.Usage.ILlmUsageService>(sp =>
                    new Atoll.Services.Usage.LlmUsageService(sp.GetRequiredService<ISettingsService>()));
                services.AddSingleton<IWindowService, WindowService>();
                services.AddSingleton<IShortcutService, ShortcutService>();
                services.AddSingleton<ITrayService>(sp =>
                    new TrayService(() => BuildTrayMenu(sp), sp.GetRequiredService<ILogger>()));
                services.AddSingleton<ShellViewModel>(sp =>
                {
                    var windowService = sp.GetRequiredService<IWindowService>();
                    var shell = new ShellViewModel(
                        sp.GetRequiredService<ISettingsService>(),
                        windowService,
                        sp.GetRequiredService<IMediaService>(),
                        sp.GetRequiredService<ISystemMetricsService>(),
                        sp.GetRequiredService<ITimerService>(),
                        sp.GetRequiredService<IClipboardService>(),
                        sp.GetRequiredService<IShelfService>(),
                        sp.GetRequiredService<INotesService>(),
                        sp.GetRequiredService<Atoll.Core.Media.ILyricsService>(),
                        sp.GetRequiredService<Atoll.Core.Usage.ILlmUsageService>(),
                        sp.GetRequiredService<ILogger>());
                    if (windowService is WindowService ws)
                        ws.Shell = shell;
                    return shell;
                });
                services.AddSingleton<Atoll.UI.MainWindow>();
            })
            .Build();

        // Expose services for cross-project access without circular references.
        Core.Contracts.ServiceLocator.Services = _host.Services;

        _host.Start();

        // Start background media monitoring (translated from MusicManager initialization).
        var mediaService = _host.Services.GetRequiredService<IMediaService>();
        _ = mediaService.StartMonitoringAsync();

        // Start system metrics polling (translated from StatsManager).
        var metricsService = _host.Services.GetRequiredService<ISystemMetricsService>();
        _ = metricsService.StartMonitoringAsync();

        // Start clipboard monitoring (translated from ClipboardManager).
        // Apply the original Defaults (clipboardHistorySize /
        // persistClipboardHistory / enableClipboardManager) and keep them live
        // when the settings window changes them.
        var clipboardService = _host.Services.GetRequiredService<IClipboardService>();
        var settingsService = _host.Services.GetRequiredService<ISettingsService>();
        ConfigureClipboard(clipboardService, settingsService);

        // These tabs default to off in the original but are on in this port; store
        // that explicitly (unless the user chose) so the settings window, which
        // falls back to the original's defaults, shows what the notch shows.
        foreach (var key in new[] { "enableStatsFeature", "enableNotes", "enableLyrics", "enableLLMUsageFeature", "showNotHumanFace" })
            if (!settingsService.Contains(key))
                settingsService.Set(key, true);

        var shell = _host.Services.GetRequiredService<ShellViewModel>();
        shell.OpenSettingsRequested += () => ShowSettingsWindow(_host!.Services);
        var mainWindow = _host.Services.GetRequiredService<Atoll.UI.MainWindow>();
        mainWindow.DataContext = shell;

        // The original is a menu-bar app: no control panel opens on launch.
        // Keep a diagnostic window behind a flag for development/testing.
        if (e.Args.Contains("--control-panel"))
            mainWindow.Show();

        if (e.Args.Contains("--settings"))
            ShowSettingsWindow(_host.Services);

        WireShortcuts(_host.Services, shell);
        WireTray(_host.Services, shell);
    }

    // ------------------------------------------------------------------
    // Clipboard (macOS ClipboardManager)
    // ------------------------------------------------------------------

    private static void ConfigureClipboard(
        IClipboardService clipboard, ISettingsService settings)
    {
        clipboard.MaxHistorySize = Math.Max(1, settings.Get("clipboardHistorySize", 3));
        clipboard.PersistHistory = settings.Get("persistClipboardHistory", true);

        settings.SettingChanged += (_, e) =>
        {
            switch (e.Key)
            {
                case "clipboardHistorySize":
                    clipboard.MaxHistorySize = Math.Max(1, Convert.ToInt32(e.NewValue ?? 3));
                    break;

                case "persistClipboardHistory":
                    clipboard.PersistHistory = e.NewValue is bool persist ? persist : true;
                    break;

                case "enableClipboardManager":
                    if (e.NewValue is bool enabled)
                    {
                        if (enabled) _ = clipboard.StartMonitoringAsync();
                        else _ = clipboard.StopMonitoringAsync();
                    }
                    break;
            }
        };

        if (settings.Get("enableClipboardManager", true))
            _ = clipboard.StartMonitoringAsync();
    }

    // ------------------------------------------------------------------
    // Tray icon (macOS MenuBarExtra)
    // ------------------------------------------------------------------

    private static void WireTray(IServiceProvider services, ShellViewModel shell)
    {
        var tray = services.GetRequiredService<ITrayService>();
        var settings = services.GetRequiredService<ISettingsService>();

        tray.ToolTip = "Atoll";
        tray.LeftClicked += (_, _) =>
        {
            // MenuBarExtra click on macOS opens the menu; on Windows the
            // notch itself is the primary surface, so toggle it.
            shell.ToggleNotchOpen(autoCloseAfter: null);
        };

        // MenuBarExtra(isInserted:) is bound to Defaults[.menubarIcon].
        tray.Visible = settings.Get("menubarIcon", true);
        settings.SettingChanged += (_, e) =>
        {
            if (e.Key != "menubarIcon") return;
            tray.Visible = e.NewValue is bool visible ? visible : true;
        };
    }

    private static ContextMenu BuildTrayMenu(IServiceProvider services)
    {
        var menu = new ContextMenu();

        var settings = new MenuItem { Header = "Settings…" };
        settings.Click += (_, _) => ShowSettingsWindow(services);
        menu.Items.Add(settings);

        var updates = new MenuItem { Header = "Check for Updates" };
        updates.Click += (_, _) => OpenUrl("https://github.com/Ebullioscopic/Atoll/releases");
        menu.Items.Add(updates);

        menu.Items.Add(new Separator());

        var restart = new MenuItem { Header = "Restart Atoll" };
        restart.Click += (_, _) =>
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] restart failed: {ex.Message}");
                }
            }
            Current.Shutdown();
        };
        menu.Items.Add(restart);

        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Current.Shutdown();
        menu.Items.Add(quit);

        return menu;
    }

    private static void ShowSettingsWindow(IServiceProvider services)
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(services.GetRequiredService<ISettingsService>());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] could not open {url}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Global shortcuts (macOS KeyboardShortcuts)
    // ------------------------------------------------------------------

    private static void WireShortcuts(IServiceProvider services, ShellViewModel shell)
    {
        var shortcuts = services.GetRequiredService<IShortcutService>();
        var settings = services.GetRequiredService<ISettingsService>();
        var timerService = services.GetRequiredService<ITimerService>();

        bool Enabled() => settings.Get("enableShortcuts", true);

        void Register(Shortcut shortcut, Action handler)
        {
            try
            {
                _ = shortcuts.RegisterAsync(shortcut, handler);
            }
            catch (Exception ex)
            {
                // A combination may already be owned by another app; keep
                // starting up rather than failing the whole application.
                Console.WriteLine($"[WARN] hotkey {shortcut.Name} unavailable: {ex.Message}");
            }
        }

        // toggleNotchOpen: ⌘⇧I on macOS → Ctrl+Shift+I.
        Register(
            new Shortcut
            {
                Name = "toggleNotchOpen",
                Key = Key.I,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Description = "Toggle Notch Open",
            },
            () =>
            {
                if (!Enabled()) return;
                var displayId = DisplayUnderCursor(services);
                shell.ToggleNotchOpen(displayId, TimeSpan.FromSeconds(3));
            });

        // clipboardHistoryPanel: ⌘⇧V on macOS → Ctrl+Shift+V.
        // Separate-tab display mode: open the notch on the Clipboard tab,
        // or close it when the Clipboard tab is already showing.
        Register(
            new Shortcut
            {
                Name = "clipboardHistoryPanel",
                Key = Key.V,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Description = "Clipboard History",
            },
            () =>
            {
                if (!Enabled() || !settings.Get("enableClipboardManager", true)) return;

                var clipboard = services.GetRequiredService<IClipboardService>();
                if (!clipboard.IsMonitoring)
                    _ = clipboard.StartMonitoringAsync();

                if (shell.CurrentView == NotchView.Clipboard &&
                    shell.Notches.Any(n => n.State == NotchState.Open))
                {
                    shell.CloseAllNotches();
                }
                else
                {
                    shell.CancelNotchAutoClose();
                    shell.SwitchTab(NotchView.Clipboard);
                }
            });

        // AI assistant: Ctrl+Shift+Space drops the assistant pill out of the
        // notch on the display under the cursor (press again or Esc to dismiss).
        Atoll.Platform.Windows.Windowing.AssistantWindow? assistant = null;
        Register(
            new Shortcut
            {
                Name = "assistant",
                Key = Key.Space,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Description = "AI Assistant",
            },
            () =>
            {
                if (!Enabled() || !settings.Get("enableAssistant", true)) return;
                assistant ??= new Atoll.Platform.Windows.Windowing.AssistantWindow(
                    services.GetRequiredService<IAssistantService>());
                var windows = services.GetRequiredService<IWindowService>() as WindowService;
                assistant.Toggle(windows?.GetNotchAnchor(DisplayUnderCursor(services)));
            });

        // startDemoTimer: ⌘⇧T on macOS → Ctrl+Shift+T.
        // Original: TimerManager.startDemoTimer(duration: 300).
        Register(
            new Shortcut
            {
                Name = "startDemoTimer",
                Key = Key.T,
                Modifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Description = "Start Demo Timer",
            },
            () =>
            {
                if (!Enabled() || !settings.Get("enableTimerFeature", true)) return;
                _ = timerService.StartAsync(TimeSpan.FromSeconds(300), "Demo Timer");
                shell.SwitchTab(NotchView.Timer);
            });
    }

    private static string? DisplayUnderCursor(IServiceProvider services)
    {
        if (!CursorPosition.TryGet(out var x, out var y)) return null;

        var windows = services.GetRequiredService<IWindowService>();
        foreach (var display in windows.Displays)
        {
            var b = display.Bounds;
            if (x >= b.Left && x < b.Right && y >= b.Top && y < b.Bottom)
                return display.Id;
        }
        return null;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(3));
            }
            catch { /* best effort */ }
            _host.Dispose();
        }
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
    }
}
