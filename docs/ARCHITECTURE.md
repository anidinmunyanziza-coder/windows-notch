# Atoll for Windows — Architecture

Source of truth: https://github.com/Ebullioscopic/Atoll (commit b52c3a7, v2.3.3)

## Stack selection

| Layer | Technology | Rationale |
|---|---|---|
| UI framework | **WPF** (.NET 10) with Win2D / Windows Composition effects | Native Windows UI framework, verified buildable on this machine (`dotnet new wpf` builds clean, 0 warnings). WinUI 3 was the roadmap's preferred direction but its project templates are not available via `dotnet new` in this environment and no Visual Studio WinUI workload is installed; bootstrapping WinUI 3 from CLI would add toolchain fragility without adding capability. WPF supports transparency, blur (DWM acrylic/mica via DwmApi), drop shadows, per-monitor DPI, and composition animations — sufficient to reproduce Atoll's design. Revisit WinUI 3 if a VS workload becomes available. |
| Language | C# 13 / .NET 10 | Latest stable, strong async, WinRT projection. |
| UI threading | WinUI `DispatcherQueue` + `async/await` | Keep UI thread free; all background work on thread pool. |
| DI | `Microsoft.Extensions.DependencyInjection` | Service registration, lifetime management. |
| Logging | `Serilog` (file + debug sink) | Structured logs, log export matching original. |
| Settings | `Windows.Storage.ApplicationData.LocalSettings` + local JSON files | Equivalent of Swift `Defaults`/`UserDefaults`, persisted across restarts. |
| IPC / single instance | Named mutex + window message | One running instance, like the macOS singleton. |
| Background | `IHostedService` + `System.Threading.Timer` / WinRT event subscriptions | Polling and event-driven monitoring. |
| Media | `Windows.Media.Control` (GSMTC) + WASAPI + per-player APIs | Global media transport controls, loopback capture, volume. |
| System metrics | PDH `PerformanceCounter` + WMI + `GlobalMemoryStatusEx` | CPU, memory, disk, network; GPU telemetry via WMI/DXGI/vendor libs. |
| Bluetooth | `Windows.Devices.Bluetooth` | LE device watcher + GATT battery service. |
| Clipboard | `Windows.ApplicationModel.DataTransfer.Clipboard` + `ContentChanged` | Clipboard history capture. |
| Global hotkeys | Win32 `RegisterHotKey` (`WM_HOTKEY`) via a hidden message window | Equivalent of macOS global shortcuts. |
| Tray | `Shell_NotifyIcon` (NOTIFYICONDATA) | Notification-area icon replacing the macOS menu bar. |
| Sessions / lock | `WTSRegisterSessionNotification` (`WM_WTSSESSION_CHANGE`) | Screen lock/unlock handling. |
| DPI | Per-monitor DPI awareness v2 | `SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)`. |
| Animations | WinUI `Composition` scalar/key-frame animations + `ConnectedAnimation` | Reproduce SwiftUI `.smooth`/spring timings. |
| Tests | `xUnit` + `Moq` + `WinAppDriver` (UI) | Unit + integration + UI automation. |

> Note: The original ships a `MediaRemoteAdapter.framework` (native) for Apple Music animated artwork and uses `SkyLightWindow` for lock-screen widgets. These are macOS-only and have no Windows equivalent; they are recorded as platform limitations in `KNOWN_DIFFERENCES.md`.

## Solution layout

```
Atoll.Windows/
├── src/
│   ├── Atoll.App/            # App.xaml, App.xaml.cs, entry, lifetime, DI container
│   ├── Atoll.Core/           # Shared contracts (interfaces), models, events, design tokens
│   ├── Atoll.UI/             # WinUI 3 views: Shell, Notch window, tabs, panels, settings
│   ├── Atoll.Services/       # Cross-platform service implementations (media, stats, clipboard, timer, shelf, settings)
│   ├── Atoll.Platform.Windows/  # Win32/WinRT interop: hotkeys, tray, DPI, sessions, Power
│   ├── Atoll.Media/          # GSMTC adapter, per-player adapters, WASAPI capture
│   ├── Atoll.SystemMonitoring/ # PDH/WMI metric collectors
│   ├── Atoll.Productivity/   # Timer, clipboard, color picker, shelf, notes, calendar
│   ├── Atoll.Infrastructure/ # DI, logging, serialization, storage, cancellation
│   └── Atoll.Theme/          # Design tokens (colors, spacing, corner radius, animation)
├── tests/
│   ├── Atoll.UnitTests/
│   ├── Atoll.IntegrationTests/
│   └── Atoll.UITests/
├── docs/
└── README.md
```

## Core abstractions

Platform-dependent services are defined as interfaces in `Atoll.Core` and implemented in `Atoll.Services` / `Atoll.Platform.Windows`. The UI and view models depend only on interfaces.

```
IMediaService          → MediaService (GSMTC + adapters)
ISystemMetricsService  → SystemMetricsService (PDH/WMI)
IClipboardService      → ClipboardService (WinRT Clipboard)
ITimerService          → TimerService (System.Threading.Timer)
IShelfService          → ShelfService (Shell32/IDataObject)
IColorPickerService    → ColorPickerService (screen pixel sampling)
INotificationService   → NotificationService (WinRT Toast)
IShortcutService       → ShortcutService (RegisterHotKey)
ITrayService           → TrayService (Shell_NotifyIcon)
ISessionService        → SessionService (WTSRegisterSessionNotification)
IPowerService          → PowerService (Battery API)
IBluetoothService      → BluetoothService (Windows.Devices.Bluetooth)
ISettingsService       → SettingsService (LocalSettings + JSON)
IWindowService         → WindowService (Win32 per-monitor windows)
```

## State management

- One **shell view model** per monitor owns `NotchState` (Closed/Hover/Open), `CurrentView` (tab), and `SneakPeek`.
- Services publish changes via C# `event` / `IObservable`-style property change (`INotifyPropertyChanged`); view models subscribe on the dispatcher thread.
- Tab order mirrors the original: `home → shelf → timer → stats → llmUsage → colorPicker → notes → clipboard → terminal → extensionExperience`.
- A settings change that affects geometry triggers `UpdateWindowSize()` (debounced), mirroring the original's `debouncedUpdateWindowSize`.

## Window / lifecycle model

- **One Win32 popup window per monitor** (`WS_POPUP`, topmost, transparent, layered), positioned at the screen top edge.
- Closed state = small fixed pill; open state = resizable to `openNotchWidth`.
- Single-instance enforced by a named mutex; second instance activates the first and exits.
- `Application.Suspending`/`Resuming` + `WM_WTSSESSION_CHANGE` handle sleep/lock.
- `WM_DISPLAYCHANGE` + `EnumDisplayMonitors` handle multi-monitor and DPI changes.

## Animation timing (translated from SwiftUI)

| Original | Windows equivalent | Duration / curve |
|---|---|---|
| `.smooth(duration: 0.25)` | Composition scalar animation | 250 ms, smooth |
| `.smooth(duration: 0.3)` | Composition scalar animation | 300 ms |
| `.interactiveSpring(dampingFraction: 1.2)` | Composition spring | damping 1.2 |
| `Animation.spring(response: 0.42, dampingFraction: 1.0)` | Composition spring | 420 ms response |
| `Animation.spring(response: 0.45, ...)` (close) | Composition spring | 450 ms response |

## Licensing

Atoll is GPL-3.0. Any code or assets adapted from the original must retain GPL-3.0 notices. Third-party dependencies (see original `project.pbxproj` SwiftPM list and `NOTICE`) carry their own licenses. See `LICENSE` and third-party license inventory in the repo.
