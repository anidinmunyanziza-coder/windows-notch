# Atoll for Windows — macOS → Windows API Mapping

Source of truth: https://github.com/Ebullioscopic/Atoll (commit b52c3a7)

Each row maps an original macOS API/behavior to the Windows equivalent, with confidence and notes. "Closest behavior" means the user-facing result is reproduced even when the underlying API differs.

| Category | macOS (Atoll) | Windows equivalent | Confidence | Notes |
|---|---|---|---|---|
| UI framework | SwiftUI | WinUI 3 (Windows App SDK) + WinUI Composition | High | Verified buildable (.NET 10). No web wrapper. |
| Reactive | Combine (`@Published`, `AnyCancellable`, `sink`) | C# `event`/`INotifyPropertyChanged` + `System.Threading.Tasks` / `IObservable` | High | UI updates marshaled to `DispatcherQueue`. |
| Layout / views | SwiftUI `VStack`, `ZStack`, `GeometryReader`, `clipShape`, `shadow` | WinUI `Grid`/`StackPanel`/`RelativePanel`, `Clip`, `DropShadow`, `CompositionBrush` | High | Proportions/spacing from `ContentView.swift` (e.g. closedNotchWidth 150, openNotchWidth 640, notchHeight 32). |
| Animation | `.smooth(duration:)`, `Animation.spring(response:dampingFraction:)`, `.interactiveSpring` | WinUI Composition scalar/key-frame/spring animations, `ConnectedAnimation` | High | Timings translated: open 420 ms / close 450 ms response, damping 1.0; hover 250 ms. |
| Window | `NSWindow` (level, space, floating, transparent) | Win32 layered popup (`WS_POPUP`, `WS_EX_LAYERED`, `WS_EX_TOPMOST`), `SetWindowPos` | High | One per monitor; positioned at screen top edge. |
| Multi-monitor | `NSScreen`, `automaticallySwitchDisplay`, `showOnAllDisplays`, `preferred_screen` | `EnumDisplayMonitors`, `GetMonitorInfo`, per-monitor DPI | High | `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2`. |
| Display scaling | `NSScreen.backingScaleFactor` | `GetDpiForMonitor` / `Windows.Graphics.Display.DisplayInformation` | High | Scale all tokens by monitor DPI. |
| Hover | `NSTrackingArea` / `CGEventTap`, `minimumHoverDuration`, edge polling | `TrackMouseEvent` (`WM_MOUSEHOVER`), timer polling hidden edges, `WM_NCMOUSEMOVE` | High | Hover suppress timer (`suppressHoverOpen`). |
| Click | `NSButton` / `onTapGesture` | `WM_LBUTTONDOWN`, WinUI `Button`/`PointerPressed` | High | |
| Gestures | SwiftUI `panGesture` (down/up/left/right), 2-finger swipe | `WM_GESTURE` / `WM_TOUCH` / touchpad precision touchpad, or low-level mouse delta | Medium | Windows gesture API surface is smaller; two-finger swipe detection via touchpad may require vendor driver. Evaluate. |
| Haptics | `NSHapticMotionManager` / `CHHapticEngine` | `Windows.Devices.Haptics` (VibrationDevice) | Medium | Fallback: no haptics (configurable `enableHaptics`). |
| Menu bar | `NSStatusItem` / `NSMenu` | `Shell_NotifyIcon` (`NOTIFYICONDATA`) notification-area tray icon | High | Tray icon + context menu replaces macOS menu bar. |
| Global hotkeys | `KeyboardShortcuts` SPM + `registerOptionalShortcutHandlers` | Win32 `RegisterHotKey` + hidden message window (`WM_HOTKEY`) | High | |
| App lifecycle | `NSApplicationDelegate` (launch, terminate, open URLs/files) | WinUI `Application` (`OnLaunched`, `OnActivated`), `WM_QUERYENDSESSION` | High | Single-instance via named mutex. |
| Sleep / lock | `NSWorkspace.didSleep/willWake`, screen locked/unlocked notifications | `WTSRegisterSessionNotification` (`WM_WTSSESSION_CHANGE`: `WTS_SESSION_LOCK`/`UNLOCK`), `WM_POWERBROADCAST` | High | Hide/restore windows on lock. |
| Display change | `NSWindow.didChangeScreen`, `screenConfigurationDidChange` | `WM_DISPLAYCHANGE`, `EnumDisplayMonitors` | High | Reposition notch windows. |
| Single instance | DistributedNotificationCenter `didBecomeActive` | Named mutex + `WM_COPYDATA`/`WM_COPYGLOBALDATA` to activate primary | High | |
| Settings persistence | `Defaults`/`UserDefaults` (plist) | `Windows.Storage.ApplicationData.LocalSettings` + local JSON | High | 374 keys extracted to `settings-schema.json`. |
| Localization | `Localizable.xcstrings` (1603 keys, 19 langs) | .resw resource files | High | |
| Media players | MediaRemote, AppleScript (Music), Spotify Web API, Cider API | GSMTC `GlobalSystemMediaTransportControlsSessionManager` (universal), Spotify Web API (+OAuth), per-player adapters | High | GSMTC covers play/pause/next/prev/seek/properties/thumbnail. Like/favorite needs per-player API (Spotify). |
| Media artwork | `NSImage`, animated artwork URL, color extraction (`avgColor`/`secondaryColor`) | `WriteableBitmap`/`SoftwareBitmap` thumbnail, `WriteableBitmap` pixel sampling | High | Video/animated artwork via Media Foundation / Win2D — more complex. |
| Lyrics | LRC / NetEase parsers, `LyricLine` (timestamp+text) | Same parsers ported to C#; lyrics only where GSMTC exposes subtitle/lyrics | High | |
| Audio / waveform | `AudioTap` (audio-unit tap), real-time FFT | WASAPI loopback capture (`IAudioClient` in loopback mode) + FFT | High | Requires audio-capture consent; original requires microphone permission. |
| Volume | `NSSound`/system volume + per-app (`PerAppVolumeManager`) | WASAPI endpoint volume (`IAudioEndpointVolume`) + per-session (`ISimpleAudioVolume`) | High | |
| CPU total | `sysctl` / CPU reader | `PerformanceCounter("Processor","% Processor Time","_Total")` | High | |
| CPU per-core | per-core reader | `PerformanceCounter("Processor","% Processor Time", <core>)` | High | |
| CPU temp / freq | SMC / IOReport bindings | WMI `MSAcpi_ThermalZoneTemperature`, `Win32_Processor.CurrentClockSpeed` | Medium | Temp/frequency not exposed on all hardware; show "—" when unavailable (never fabricate). |
| GPU | IOKit `IOAccelerator` `PerformanceStatistics` | WMI `MSAcpi_ThermalZone`, DXGI GPU usage, NVML/ADL optional | Medium | Discrete GPU telemetry varies by vendor; fallback to WMI. |
| Memory | `MemoryBreakdown` (mach task_info / host_statistics) | `GlobalMemoryStatusEx` + `PerformanceCounter("Memory")` | High | `MemoryPressure` level approximated from available %; original "wired/active/inactive/compressed" are macOS-specific → report used/available/commit. |
| Network | `NetworkConnectivityManager` | `PerformanceCounter("Network Interface","Bytes Total/sec")` + `Windows.Networking.Connectivity.NetworkInformation` | High | |
| Disk | `PhysicalDisk` reader | `PerformanceCounter("PhysicalDisk","Disk Bytes/sec")` | High | |
| Battery | `IOKit.ps` (`IOPSCopyPowerSourcesInfo`), `MacBatteryManager` | `Windows.System.Power.Battery`, `PowerManager` | High | |
| Bluetooth | `IOBluetooth` (`BluetoothAudioManager`) | `Windows.Devices.Bluetooth` (`BluetoothLEDevice`, `DeviceWatcher`), GATT Battery Service | High | LE only; classic-device battery inconsistent. |
| Audio routes | `AudioRouteManager` | WASAPI `IMMDeviceEnumerator` endpoints | High | |
| Per-app volume | `PerAppVolumeManager` | WASAPI `ISimpleAudioVolume` per audio session | High | |
| Caffeinate | `CaffeinateManager` (`caffeinate` / `IOPMAssertion`) | `SetThreadExecutionState(ES_CONTINUOUS\|ES_SYSTEM_REQUIRED\|ES_DISPLAY_REQUIRED)` | High | |
| Screen recording detection | `ScreenRecordingManager` (CGPreflightScreenCaptureAccess) | No direct equivalent; heuristic via `Windows.Graphics.Capture` availability / process enumeration | Low | PLATFORM LIMITATION — document gap. |
| Camera/mic privacy | `CameraMonitor`/`MicrophoneMonitor` (access checks) | No direct equivalent; process/handle enumeration heuristic | Low | PLATFORM LIMITATION — document gap. |
| DND / Focus | `DoNotDisturbManager` | Windows Focus Assist not directly readable by third parties | Low | PLATFORM LIMITATION — document gap. |
| Lock-screen widgets | `SkyLightWindow`, `components/LockScreen/` | None — Windows lock screen not extensible | None | PLATFORM LIMITATION — remove or replace with desktop widget. |
| Menu bar / notch | macOS notch geometry | No notch on Windows → floating top-edge pill | None | PLATFORM LIMITATION — reproduced as floating surface, not a notch. |
| LocalSend | `LocalSendService` (LAN discovery) | UDP/TCP LAN discovery sockets | Medium | Optional; evaluate. |
| Terminal | `SwiftTerm` (pty) + terminal control | Win32 ConPTY + terminal rendering control | Medium | High complexity; evaluate scope. |
| Calendar | `CalendarManager` (EventKit) / third-party | No direct Win32 calendar API; `.ics` parse or WinRT `AppointmentStore` (limited) | Low | PLATFORM LIMITATION — document gap. |
| Extensions | `AtollExtensionKit`, XPC service, RPC server | Out-of-process extensibility via named pipes / .NET add-in model | Low | Future work; document. |
| Updates | Sparkle (`SUFeedURL` appcast, ED key) | GitHub Releases API + auto-downloader | Medium | Replace Sparkle with simple GitHub release check. |
| Crash/logging | `logLevel`, `exportLogs` | Serilog/NLog file sink + export | High | |
| Sounds | `.m4a/.mp3` (lock, unlock, timer, lowbattery, audio-feedback, dynamic) | `MediaPlayer`/`SoundPlayer` WAV/MP3 | High | Repackage sounds; check licenses. |
| Lottie animations | `LottieUI` SPM (JSON) | Lottie-Windows / Skia | High | Bundled JSON files can be reused (check Lottie Simple License for FingerprintScan). |

## Known unavoidable differences (summary)

1. **Notch geometry** — Windows PCs have no notch; the floating pill sits at the top edge of each monitor.
2. **Menu bar** — replaced by notification-area tray icon.
3. **Lock-screen widgets** — not supported on Windows; removed (documented).
4. **Camera/mic/screen-recording privacy indicators** — no direct Windows API; heuristic only (documented).
5. **Focus Assist / DND state** — not readable by third-party apps (documented).
6. **Apple Music animated lock-screen wallpaper / MediaRemote** — macOS-only; GSMTC replaces universal media control but not AppleScript-specific features.
7. **Classic Bluetooth battery** — inconsistent on Windows (LE/GATT works).
8. **Terminal (SwiftTerm)** — high complexity to port; evaluate before committing.
