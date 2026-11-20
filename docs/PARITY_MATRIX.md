# Atoll for Windows — Parity Matrix

Source of truth: https://github.com/Ebullioscopic/Atoll (commit b52c3a7, version 2.3.3)
Investigation location: `C:\Users\Anidin\projects\atoll-windows\original`

Statuses:
- **NOT ANALYZED** — not yet examined in the original repo
- **ANALYZED** — original implementation read and understood
- **IMPLEMENTATION STARTED** — Windows port code written, not complete
- **IMPLEMENTED** — Windows port code complete, not yet tested
- **FUNCTIONALLY VERIFIED** — behavior tested against original
- **VISUALLY VERIFIED** — appearance tested against original
- **FULLY VERIFIED** — both behavior and appearance verified
- **BLOCKED** — implementation prevented by a technical issue
- **PLATFORM LIMITATION** — no exact Windows equivalent; closest behavior documented in `KNOWN_DIFFERENCES.md`

Legend: Orig = original analyzed | Win = Windows implemented | Fn = functional test | Vis = visual test

---

## 5.1 Dynamic Island / Main Interface

| Feature | Orig | Win | Fn | Vis | Status | Original source | Windows strategy |
|---|---|---|---|---|---|---|---|
| Main notch window (per-display) | Yes | Yes | Pending | Pending | IMPLEMENTED | `ContentView.swift`, `DynamicIslandViewModel.swift`, `DynamicIslandApp.swift` | WPF Window per monitor (topmost, transparent, layered), `EnumDisplayMonitors` |
| Closed state (collapsed pill) | Yes | Yes | Pending | Pending | IMPLEMENTED | `ContentView.swift` NotchLayout, `closedNotchWidth` (150), `notchHeight` (32) | Fixed-size WPF window, rounded rect clip, DPI-scaled |
| Hover-to-open | Yes | Yes | Pending | Pending | IMPLEMENTED | `handleHover`, `minimumHoverDuration` (0.3), `suppressHoverOpen` | `MouseEnter`/`MouseLeave` + DispatcherTimer (0.3s) |
| Click-to-open | Yes | Yes | Pending | Pending | IMPLEMENTED | `onTapGesture` → `openNotch()` | `MouseLeftButtonDown` → toggle |
| Open/expanded state | Yes | Yes | Pending | Pending | IMPLEMENTED | `openNotchWidth` (640), `notchState == .open` | NotchViewModel.State Open/Closed, window resized |
| Expand/collapse animation | Yes | Yes | Partial | Pending | IMPLEMENTED | `Animation.spring(response: 0.42/0.45, dampingFraction: 1.0)`, `.smooth(duration: 0.25/0.3)` | WPF DoubleAnimation (opacity 250ms), window resize |
| Tab navigation (home/shelf/timer/stats/llmUsage/colorPicker/notes/clipboard/terminal/extensionExperience) | Yes | Yes | Pending | Pending | IMPLEMENTED | `NotchViews` enum, `tabOrder`, `tabSwitchForward` | `NotchView` enum, `ShellViewModel.Tabs`, `SwitchTab` |
| Tab switch animation direction | Yes | Partial | Pending | Pending | IMPLEMENTED | `tabSwitchTransition` blurReplace + spring | `tabSwitchForward` tracked, content swap |
| Hover detection region | Yes | Yes | Pending | Pending | IMPLEMENTED | `extendHoverArea`, edge-hover polling (`startHiddenEdgeHoverPolling`) | `MouseEnter`/`MouseLeave` on notch window |
| Gesture: 2-finger swipe down to open | Yes | No | Pending | Pending | ANALYZED | `handleDownGesture` (enableGestures) | Planned |
| Gesture: swipe up to close | Yes | No | Pending | Pending | ANALYZED | `handleUpGesture` | Planned |
| Gesture: horizontal media skip | Yes | No | Pending | Pending | ANALYZED | `handleSkipGesture(direction: forward/backward)` | Planned |
| Haptics on tap | Yes | No | Pending | Pending | ANALYZED | `triggerHapticIfAllowed` (enableHaptics) | Planned (`Windows.Devices.Haptics`) |
| Auto-close when hover leaves | Yes | Yes | Pending | Pending | IMPLEMENTED | `vm.close()` on `!isHovering` | `MouseLeave` → `CloseNotch()` |
| Hidden-until-hover (`hideNonNotchUntilHover`) | Yes | No | Pending | Pending | ANALYZED | negative Y offset until hover | Planned |
| Multi-monitor (one notch per display) | Yes | Yes | Pending | Pending | IMPLEMENTED | `showOnAllDisplays`, `automaticallySwitchDisplay`, `preferred_screen` | `EnumDisplayMonitors`, one WindowService-managed window per display |
| Display scaling (DPI) | Yes | Yes | Pending | Pending | IMPLEMENTED | `NSScreen` backing scale | `GetDpiForMonitor`, per-monitor scale applied to geometry |
| Taskbar/fullscreen awareness | Yes | No | Pending | Pending | ANALYZED | `alwaysHideInFullscreen`, `hideDynamicIslandFromScreenCapture` | Planned (`APPBARDATA`, `GetWindowRect`) |
| Notch corner radius | Yes | Yes | Pending | Pending | IMPLEMENTED | `cornerRadiusScaling` | WPF `CornerRadius` (16 closed / 12 open) |
| Shadow | Yes | Yes | Pending | Pending | IMPLEMENTED | `shadow(color:opacity:radius:)` radius 5/10 | WPF `DropShadowEffect` (opacity 0.6, blur 10) |
| Minimalistic UI mode | Yes | No | Pending | Pending | ANALYZED | `enableMinimalisticUI` forces home tab | Planned |
| Settings icon in notch | Yes | No | Pending | Pending | ANALYZED | `settingsIconInNotch` | Planned |
| Context menu (right-click → Settings) | Yes | No | Pending | Pending | ANALYZED | `contextMenu { Button("Settings") }` | WPF ContextMenu planned |

## 5.2 Media Controls

| Feature | Orig | Win | Fn | Vis | Status | Original source | Windows strategy |
|---|---|---|---|---|---|---|---|
| Media player detection (Apple Music, Spotify, Cider, YouTube Music, Amazon, Tidal, etc.) | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `MusicManager.swift`, `MediaControllers/`, `SystemMediaControllers.swift` | GSMTC `GlobalSystemMediaTransportControlsSessionManager` + per-player adapters |
| Play/pause | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | MusicManager playback commands | `GSMTCSession.TryTogglePlayPauseAsync()` |
| Next/previous track | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | skip behavior (track vs +/-10s) config | `Next`/`Previous`, plus SeekForward/SeekBackward 10s |
| Seek / playback progress | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `elapsedTime`, `songDuration`, `playbackRate` | `TryChangePlaybackPositionAsync(ticks)` + timeline polling |
| Track/artist/album/artwork | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `songTitle`, `artistName`, `album`, `albumArt` | `TryGetMediaPropertiesAsync` + thumbnail |
| Playback status (playing/paused/idle) | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `isPlaying`, `isPlayerIdle` | `PlaybackInfo.PlaybackStatus` |
| Shuffle/repeat | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `isShuffled`, `repeatMode` | GSMTC `IsShuffleActive`/`AutoRepeatMode` |
| Like/favorite | Yes | No | Pending | Pending | ANALYZED | `isCurrentTrackLiked`, `canFavoriteCurrentTrack` | Not in GSMTC; per-player API (Spotify Web API) |
| Volume control | Yes | No | Pending | Pending | ANALYZED | `volumeStepPercent`, `enableExternalVolumeControlListener` | GSMTC volume + WASAPI endpoint volume |
| Lyrics (synced/un synced) | Yes | No | Pending | Pending | ANALYZED | `LyricLine`, `currentLyrics`, `syncedLyrics`, LRC/NetEase parsers | GSMTC `Subtitle`/`Lyrics` where available; else none |
| Animated/video artwork | Yes | No | Pending | Pending | ANALYZED | `videoArtworkURL`, `AnimatedArtworkManager` | Media Foundation / Win2D video in WinUI |
| Artwork color extraction (avg/secondary) | Yes | No | Pending | Pending | ANALYZED | `avgColor`, `secondaryColor` | `WriteableBitmap` color sampling |
| Media control window (floating) | Yes | No | Pending | Pending | ANALYZED | `MusicControlWindowManager` | WinUI `Window` floating |
| Closed-notch music display | Yes | No | Pending | Pending | ANALYZED | `MusicLiveActivity`, marquee title/artist | Collapsed Win32 window showing text |
| Real-time waveform / spectrogram | Yes | No | Pending | Pending | ANALYZED | `AudioTap.swift`, `enableRealTimeWaveform` | WASAPI loopback capture + FFT |
| Player switching / priority | Yes | No | Pending | Pending | ANALYZED | `bundleIdentifier`, player priority | GSMTC session ordering |
| Apple Music animated artwork lock-screen wallpaper | Yes | No | Pending | Pending | PLATFORM LIMITATION | macOS lock-screen wallpaper API | Not available on Windows; documented gap |
| Spotify auth (OAuth + token store) | Yes | No | Pending | Pending | ANALYZED | `SpotifyAuthManager`, `SpotifyTokenStore`, `SpotifyHTTPClient` | WebAuthenticator + secure storage |
| iTunes explicitness lookup | Yes | No | Pending | Pending | ANALYZED | `MusicExplicitnessResolver` (iTunes Search API) | Web API call (reusable) |

## 5.3 System Monitoring

| Feature | Orig | Win | Fn | Vis | Status | Original source | Windows strategy |
|---|---|---|---|---|---|---|---|
| CPU utilization (total + per-core) | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `StatsManager.swift`, `CPUMetrics.swift` | PDH `PerformanceCounter("Processor","% Processor Time","_Total")`; per-core enumeration not available in current PDH package (documented gap) |
| CPU temperature | Yes | No | Pending | Pending | ANALYZED | `cpuTemperatureUnit`, SMC/IOReport | OpenHardwareLib / WMI `MSAcpi_ThermalZoneTemperature` (limited) |
| CPU frequency | Yes | No | Pending | Pending | ANALYZED | IOReport bindings | WMI `Processor` / `Win32_Processor` `CurrentClockSpeed` |
| GPU utilization / temp / clocks / fan | Yes | No | Pending | Pending | ANALYZED | `GPUInfoCollector` (IOKit IOAccelerator PerformanceStatistics) | WMI `MSAcpi_ThermalZone`, DXGI GPU usage, vendor libs (NVML/ADL) optional |
| Memory usage (total/used/free/wired/compressed/pressure) | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `MemoryBreakdown` | PDH `Memory\Available MBytes` + `Committed Bytes`, pressure approximated (wired/active/inactive/compressed are macOS-specific) |
| Network activity (bytes/s, interface) | Yes | No | Pending | Pending | ANALYZED | `NetworkConnectivityManager`, `showNetworkGraph` | PDH per-instance enumeration not available in package; returns null |
| Disk activity (bytes/s) | Yes | No | Pending | Pending | ANALYZED | `showDiskGraph` | PDH per-instance enumeration not available in package; returns null |
| Stats polling interval | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `statsUpdateInterval`, `autoStartStatsMonitoring` | `System.Threading.Timer` configurable, defaults 1s |
| Stats start/stop with notch open/closed | Yes | No | Pending | Pending | ANALYZED | `updateMonitoringState(notchIsOpen:currentView:)`, `statsStopWhenNotchCloses` | Planned - wire to WindowService state |
| Memory pressure | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `MemoryPressure` | Approximated from used % (Normal/Warning/Critical) |
| Graph history (rolling) | Yes | No | Pending | Pending | ANALYZED | `showCpuGraph` etc., graph views | Rolling circular buffer per metric |

## 5.4 Live Activities

| Feature | Orig | Win | Fn | Vis | Status | Original source | Windows strategy |
|---|---|---|---|---|---|---|---|
| Media playback live activity | Yes | No | Pending | Pending | ANALYZED | `MusicLiveActivity`, `components/Live activities` | Collapsed-window HUD + GSMTC subscription |
| Timer live activity | Yes | No | Pending | Pending | ANALYZED | `TimerLiveActivity`, `TimerManager` | WinUI notification / toast + collapsed HUD |
| Reminder live activity | Yes | No | Pending | Pending | ANALYZED | `ReminderLiveActivity`, `ReminderLiveActivityManager` | WinRT `AppointmentStore` / local reminders |
| Screen recording indicator | Yes | No | Pending | Pending | ANALYZED | `ScreenRecordingManager`, `enableScreenRecordingDetection` | `GetCursorInfo`/screen capture detection not directly available; `WM_DWMCOLORIZATIONCOLORCHANGED` not sufficient. Closest: query `Windows.Graphics.Capture` availability / process enumeration; documented partial. |
| Privacy indicators (camera/mic) | Yes | No | Pending | Pending | ANALYZED | `PrivacyIndicatorManager`, `CameraMonitor`, `MicrophoneMonitor` | No direct Windows API; process/handle enumeration heuristic; documented PLATFORM LIMITATION |
| Do Not Disturb / Focus indicator | Yes | No | Pending | Pending | ANALYZED | `DoNotDisturbManager`, `focusMonitoringMode` | Windows Focus Assist not directly readable; document gap |
| Battery / charging live activity | Yes | No | Pending | Pending | ANALYZED | `BatteryActivityManager`, `MacBatteryManager`, `BatteryPowerReading` | `Windows.System.Power.Battery` + `PowerManager` |
| Bluetooth device live activity / HUD | Yes | No | Pending | Pending | ANALYZED | `BluetoothAudioManager`, `BluetoothAudioDevice` | `Windows.Devices.Bluetooth` + `BluetoothLEDevice` |
| Download live activity | Yes | No | Pending | Pending | ANALYZED | `DownloadManager`, `DownloadLiveActivity` | `DownloadManager` folder watcher / `ITransferManager` |
| LocalSend live activity | Yes | No | Pending | Pending | ANALYZED | `LocalSendLiveActivity`, `LocalSendService` | LAN discovery via sockets (optional) |
| Extension live activities | Yes | No | Pending | Pending | ANALYZED | `ExtensionLiveActivityManager`, `AtollExtensionKit` | Third-party extensibility; documented as future |

## 5.5 Productivity Utilities

| Feature | Orig | Win | Fn | Vis | Status | Original source | Windows strategy |
|---|---|---|---|---|---|---|---|
| Timer (countdown, presets, display modes) | Yes | Yes | Pending | Pending | IMPLEMENTATION STARTED | `TimerManager.swift`, `TimerPreset.swift`, `TimerDisplayMode.swift` | `System.Threading.Timer` + monotonic deadline (sleep/resume safe), 6 presets |
| Clipboard history (capture, dedupe, filter, persistence, drag-out) | Yes | No | Pending | Pending | ANALYZED | `ClipboardManager.swift`, `components/Clipboard/` | `Windows.ApplicationModel.DataTransfer.Clipboard` + `ContentChanged` |
| Color picker (screen sample, formats, history) | Yes | No | Pending | Pending | ANALYZED | `ColorPickerManager.swift`, `PickedColor.swift` | `ScreenCapture`/`GraphicsCapturePicker` pixel sampling + format conversion |
| Calendar previews (events, countdown) | Yes | No | Pending | Pending | ANALYZED | `CalendarManager.swift`, `CalendarModel.swift`, `EventModel.swift` | No direct Win32 calendar API; `.ics` parsing or WinRT `AppointmentStore` (limited); documented gap |
| File Shelf (add files, selection, context menu, drag, persistence) | Yes | No | Pending | Pending | ANALYZED | `components/Shelf/`, `ShelfItem.swift`, `Bookmark.swift`, `drop.swift` | `IDataObject` drag-drop, `.lnk`/shortcut storage, `Shell32` |
| Notes (pinning, search, color filter, char count) | Yes | No | Pending | Pending | ANALYZED | `savedNotes`, `enableNotes` components | Local JSON storage + WinUI editor |
| Terminal (pty, shell, scrollback, opacity) | Yes | No | Pending | Pending | ANALYZED | `TerminalManager.swift`, `SwiftTerm` | Win32 ConPTY + a terminal rendering control (complex; evaluate) |
| Screen Assistant (AI screen capture assistant) | Yes | No | Pending | Pending | ANALYZED | `ScreenAssistantManager.swift`, `components/ScreenAssistant/` | Requires screen capture + LLM provider; evaluate |
| LLM usage tracking (providers, quota, pricing) | Yes | No | Pending | Pending | ANALYZED | `managers/LLMUsage/` | Local log parsing where files exist on Windows |

## 5.6 Bluetooth

| Feature | Orig | Win | Fn | Vis | Status | Original source | Windows strategy |
|---|---|---|---|---|---|---|---|
| Connected device detection | Yes | No | Pending | Pending | ANALYZED | `BluetoothAudioManager` (2542 lines) | `Windows.Devices.Bluetooth` |
| Device battery levels | Yes | No | Pending | Pending | ANALYZED | `showBluetoothBatteryPercentageText`, `useBluetoothHUD3DIcon` | GATT Battery Service |
| Connection state / device changes | Yes | No | Pending | Pending | ANALYZED | `BluetoothAudioDevice`, `DeviceChangesObserver` | `DeviceWatcher` + `BluetoothLEDevice` |
| Audio device interactions | Yes | No | Pending | Pending | ANALYZED | `AudioRouteManager`, `PerAppVolumeManager` | WASAPI audio endpoints + `IMMDeviceEnumerator` |
| Bluetooth HUD | Yes | No | Pending | Pending | ANALYZED | HUD window managers | WinUI floating HUD |

## 5.7 Customization & Settings

| Feature | Orig | Win | Fn | Vis | Status | Original source | Windows strategy |
|---|---|---|---|---|---|---|---|
| Full settings tree (~38 sections, 374 keys) | Yes | No | Pending | Pending | ANALYZED | `models/Constants.swift` (`extension Defaults.Keys`), `components/Settings/` | Extracted to `docs/notes/settings-schema.json`; WPF `SettingsWindow` per section backed by `ISettingsService` |
| Appearance (accent color, shadow, corner radius, gradient) | Yes | No | Pending | Pending | ANALYZED | `Appearance` section keys | WinUI color pickers + `CompositionBrush` |
| Animation style (modern close, wobble) | Yes | No | Pending | Pending | ANALYZED | `useModernCloseAnimation`, `enableWobbleAnimation` | Animation selection in WinUI |
| Layout customization (notch width/height, position) | Yes | No | Pending | Pending | ANALYZED | `openNotchWidth`, `closedNotchWidth`, `notchHeight` | Settings sliders applying to Win32 window size |
| Hover behavior (duration, extend area) | Yes | No | Pending | Pending | ANALYZED | `minimumHoverDuration`, `extendHoverArea` | Settings applied to hover tracker |
| Gesture controls | Yes | No | Pending | Pending | ANALYZED | `enableGestures`, `gestureSensitivity`, `reverseSwipeGestures` | Settings applied to gesture engine |
| Global keyboard shortcuts | Yes | No | Pending | Pending | ANALYZED | `KeyboardShortcuts` usage, `registerOptionalShortcutHandlers` | `RegisterHotKey` / `WM_HOTKEY` |
| Feature toggles | Yes | No | Pending | Pending | ANALYZED | 300+ `enable*` keys | Settings checkboxes enabling/disabling services |
| Lock screen widgets | Yes | No | Pending | Pending | PLATFORM LIMITATION | `components/LockScreen/`, `SkyLightWindow` | Windows lock screen is not extensible by third parties; documented gap |
| Media / Stats / Timer / Clipboard config | Yes | No | Pending | Pending | ANALYZED | per-feature settings sections | Settings pages per feature |
| Notification behavior | Yes | No | Pending | Pending | ANALYZED | `showPowerStatusNotifications`, HUD durations | WinUI notifications + HUD timers |
| Persistence across restart | Yes | No | Pending | Pending | ANALYZED | `UserDefaults`/`Defaults` (Swift) | `Windows.Storage.ApplicationData.Current.LocalSettings` + local files |

## 5.8 Additional / Background Features

| Feature | Orig | Win | Fn | Vis | Status | Original source | Windows strategy |
|---|---|---|---|---|---|---|---|
| App lifecycle (launch, sleep/wake, display change, terminate) | Yes | No | Pending | Pending | ANALYZED | `DynamicIslandApp.swift` `AppDelegate` | `Application.Suspending/Resuming`, `SystemEvents.DisplaySettingsChanged` |
| Startup / launch-at-login | Yes | No | Pending | Pending | ANALYZED | `LaunchAtLogin-Modern` SPM dependency | Windows Startup folder / registry Run key |
| Menu bar (status item) | Yes | No | Pending | Pending | ANALYZED | `MenuBarLayout.swift`, `menubarIcon` | Windows notification-area (tray) icon via `Shell_NotifyIcon` |
| Global shortcuts | Yes | No | Pending | Pending | ANALYZED | `registerOptionalShortcutHandlers` | `RegisterHotKey` window message pump |
| Screen lock/unlock handling (hide/restore windows) | Yes | No | Pending | Pending | ANALYZED | `onScreenLocked`/`onScreenUnlocked`, `hideWindowsForLock` | `WTSRegisterSessionNotification` + `WM_WTSSESSION_CHANGE` |
| Permissions (accessibility, screen recording, camera, calendar) | Yes | No | Pending | Pending | ANALYZED | entitlements + usage descriptions | Win32 manifest capabilities + runtime consent where applicable |
| Background services (media monitoring, stats polling, clipboard) | Yes | No | Pending | Pending | ANALYZED | managers started in `applicationDidFinishLaunching` | `BackgroundWorker`/`IHostedService` + `Win32` service or keep-alive window |
| Update checking (Sparkle) | Yes | No | Pending | Pending | ANALYZED | `UpdateChannel.swift`, `SUFeedURL` appcast | GitHub releases API + `INotify`/auto-downloader |
| App icon customization (Alpha/Beta/Dev/Nightly) | Yes | No | Pending | Pending | ANALYZED | `selectedAppIconID`, `AppIcon*.appiconset` | WinUI `SetAppId` / alternate icon not supported; document |
| Localization (19 languages, 1603 keys) | Yes | No | Pending | Pending | ANALYZED | `Localizable.xcstrings` | .resw resource files |
| Crash/logging / log export | Yes | No | Pending | Pending | ANALYZED | `logLevel`, `exportLogs` | `Serilog`/`NLog` + file export |
| Third-party extensions (extension kit, RPC server, XPC) | Yes | No | Pending | Pending | ANALYZED | `managers/Extensions/`, `AtollExtensionKit` | Out-of-process extensibility; document as future |
| Fullscreen media detection | Yes | No | Pending | Pending | ANALYZED | `FullScreenMediaDetection.swift` | Foreground window tracking + `GetShellWindow` |
| Per-app volume | Yes | No | Pending | Pending | ANALYZED | `PerAppVolumeManager`, `audio/` | WASAPI per-session volume (`ISimpleAudioVolume`) |
| Caffeinate (sleep prevention) | Yes | No | Pending | Pending | ANALYZED | `CaffeinateManager` | `SetThreadExecutionState(ES_CONTINUOUS\|ES_SYSTEM_REQUIRED)` |
| Audio tap / real-time waveform | Yes | No | Pending | Pending | ANALYZED | `AudioTap.swift` | WASAPI loopback capture |
| Network connectivity HUD | Yes | No | Pending | Pending | ANALYZED | `NetworkConnectivityManager`, `NetworkConnectivityHUD` | `NetworkInformation.NetworkStatusChanged` |
| Idle animations / face animation | Yes | No | Pending | Pending | ANALYZED | `IdleAnimationManager`, `DynamicIslandFaceAnimation`, Lottie | Lottie-Windows / Skia animation playback |
| Shelf file actions | Yes | No | Pending | Pending | ANALYZED | `ShelfServices`, drag-out | Shell file operations |

---

## Data model notes (from original)

- `MusicManager` published state: `songTitle`, `artistName`, `albumArt`, `albumArtGlowTexture`, `isPlaying`, `album`, `isPlayerIdle`, `songDuration`, `elapsedTime`, `playbackRate`, `isShuffled`, `repeatMode`, `currentLyrics`, `syncedLyrics:[LyricLine]`, `isCurrentTrackLiked`, `canFavoriteCurrentTrack`, `bundleIdentifier`, `videoArtworkURL`, `flipAngle`, `isTransitioning`.
- `StatsManager` models: `MemoryBreakdown` (total/used/free/wired/active/inactive/compressed/app/cache/swap/pressure), `CPUCoreUsage(id, usage)`, `GPUDeviceMetrics` (vendor/model/utilization/temp/fan/coreClock/memoryClock/cores).
- `SneakContentType`: brightness, volume, backlight, music, mic, battery, download, timer, reminder, recording, doNotDisturb, bluetoothAudio, privacy, lockScreen, capsLock, extensionLiveActivity.
- `NotchViews`: home, shelf, timer, stats, llmUsage, colorPicker, notes, clipboard, terminal, extensionExperience.

---

## Settings schema

Extracted from `models/Constants.swift` (`extension Defaults.Keys`) → `docs/notes/settings-schema.json` / `.md`. 374 keys across 38 sections. See that file for the complete key → type → default mapping. Every Windows setting must map to one of these keys (or document why not).
