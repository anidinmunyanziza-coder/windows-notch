# Atoll for Windows — Feature Inventory

Source of truth: https://github.com/Ebullioscopic/Atoll (commit b52c3a7, v2.3.3)
Investigation location: `C:\Users\Anidin\projects\atoll-windows\original`
Settings schema extracted: `docs/notes/settings-schema.json` (374 keys / 38 sections from `models/Constants.swift`)

Each feature documents: name, description, original source files, key classes/structs, UI components, interactions, background operations, OS dependencies, data flow, state, permissions, Windows strategy, and test requirements.

---

## 1. Main Interface (Dynamic Island / Notch)

- **Description:** Floating per-monitor surface at the screen top edge. Closed pill expands on hover/click to reveal tabs. Live activities (media, timer, battery, recording, etc.) render in the closed state.
- **Original source:** `ContentView.swift` (NotchLayout, body), `DynamicIslandViewModel.swift`, `DynamicIslandViewCoordinator.swift`, `DynamicIslandApp.swift` (window creation), `managers/MenuBarLayout.swift`, `managers/NotchSpaceManager.swift`.
- **Key classes/structs:** `ContentView`, `NotchViews` enum, `DynamicIslandViewModel` (vm, notchState), `DynamicIslandViewCoordinator` (currentView, tabOrder), `sneakPeek`, `ExpandedItem`.
- **UI components:** NotchHomeView, NotchShelfView, NotchTimerView, NotchStatsView, NotchLLMUsageView, NotchColorPickerView, NotchNotesView, NotchClipboardView, NotchTerminalView, ExtensionNotchExperienceTabView, DynamicIslandHeader, InlineHUD, BatteryTemporaryActivityView, MusicLiveActivity, TimerLiveActivity, ReminderLiveActivity, RecordingLiveActivity, DownloadLiveActivity, LocalSendLiveActivity, DoNotDisturbLiveActivity, PrivacyLiveActivity, ExtensionLiveActivityStandaloneView, ShelfInlineLiveActivity, DynamicIslandFaceAnimation, MarqueeText.
- **Interactions:** hover (minimumHoverDuration 0.3s), click to open, 2-finger swipe down/up, horizontal skip gestures, tap haptics, context menu → Settings.
- **Background:** hidden-edge hover polling (`startHiddenEdgeHoverPolling`), stats monitoring toggle on notch open/close, window resize on tab switch.
- **OS deps:** NSScreen per-display, NSWindow level/space, CGEventTap (hover), Accessibility permission.
- **Data flow:** Hover event → coordinator/isHovering → vm.notchState = .open → view body switches tab view → window resized via `updateWindowSizeForTabSwitch`.
- **State:** notchState (closed/open), currentView (tab), tabSwitchForward, sneakPeek, expandingView.
- **Permissions:** Accessibility (global events), optional Screen Recording.
- **Windows strategy:** One transparent layered Win32 popup per monitor at top edge; `TrackMouseEvent` + edge polling for hover; Win32 `WM_LBUTTONDOWN` for click; WinUI Composition animations for expand/collapse; `RegisterHotKey`/gestures for swipe.
- **Tests:** window creation per monitor, hover open/close, tab switch resize, closed pill dimensions.

---

## 2. Media Controls

- **Description:** Now-playing HUD + controls for Apple Music, Spotify, Cider, YouTube Music, Amazon, Tidal, etc. Playback state, artwork, lyrics, like/favorite, volume, progress, player switching, real-time waveform.
- **Original source:** `managers/MusicManager.swift` (2009 lines), `managers/SystemMediaControllers.swift`, `MediaControllers/` (all), `managers/Spotify/`, `managers/MediaKeyInterceptor.swift`, `managers/MusicControlWindowManager.swift`, `managers/AnimatedArtworkManager.swift`, `models/PlaybackState.swift`, `models/MusicControlButton.swift`, `managers/LyricsMetadata.swift`, `LRCParser.swift`, `NetEaseLyrics.swift`, `audio/` (AudioTap).
- **Key classes/structs:** `MusicManager` (songTitle, artistName, albumArt, isPlaying, isPlayerIdle, songDuration, elapsedTime, currentLyrics, syncedLyrics:[LyricLine], isCurrentTrackLiked, canFavoriteCurrentTrack, bundleIdentifier, videoArtworkURL, flipAngle), `MusicExplicitnessResolver`, `LyricLine`, `LyricsLookupKey`.
- **UI components:** MusicLiveActivity, closed-notch music marquee, artwork view with color glow (avgColor/secondaryColor), parallax, flip animation on skip, lyrics panel, visualizer bars.
- **Interactions:** play/pause, next/prev, seek, +/-10s gesture skip, shuffle/repeat, like, volume, output device, open music control window.
- **Background:** per-player polling/subscription, advertisement detection, artwork fetch + color extraction, explicitness lookup (iTunes API cache 300), audio-tap capture when `enableRealTimeWaveform`.
- **OS deps:** MediaRemote, AppleScript/automation (Music), Spotify Web API + OAuth, Cider API, Accessibility for media keys.
- **Data flow:** Player event → MusicManager updates @Published properties → ContentView rebuilds MusicLiveActivity → window resized if artwork/lyrics height changed.
- **State:** playback status, track metadata, progress, like state, lyrics availability, transition/flip state.
- **Permissions:** Music (AppleScript), Spotify (OAuth), microphone/audio capture (waveform).
- **Windows strategy:** GSMTC `GlobalSystemMediaTransportControlsSessionManager` for universal play/pause/next/prev/seek/properties; Spotify Web API for like/favorite and token storage (`SpotifyTokenStore` equivalent); WASAPI loopback + FFT for waveform; `WriteableBitmap` color sampling; per-monitor floating music control window.
- **Tests:** GSMTC session enumeration, play/pause/next/prev, property mapping, artwork download, lyrics parse, waveform capture on/off.

---

## 3. System Monitoring (Stats)

- **Description:** CPU (total + per-core, temp, frequency), GPU (util, temp, fan, clocks), memory (breakdown + pressure), network, disk. Rolling graphs. Polling pauses when notch closed per `statsStopWhenNotchCloses`.
- **Original source:** `managers/StatsManager.swift` (1457 lines), `models/CPUMetrics.swift`, `models/MemoryBreakdown`, `components/Stats/`, `helpers/` (IOKit readers).
- **Key classes/structs:** `StatsManager`, `MemoryBreakdown` (total/used/free/wired/active/inactive/compressed/app/cache/swap/pressure, usedPercentage), `MemoryPressure` (level), `CPUCoreUsage` (id, usage), `GPUDeviceMetrics` (vendor/model/utilization/temp/fanSpeed/coreClock/memoryClock/cores), `GPUInfoCollector`.
- **UI components:** Stats rows + graphs (cpu/memory/gpu/network/disk), per-core bars, stats second-row expansion (`statsSecondRowExpansion`).
- **Interactions:** enable/disable categories in Settings, graph visibility toggles.
- **Background:** `MemoryUsageMonitor.shared.startMonitoring()`, polling interval `statsUpdateInterval`, `updateMonitoringState(notchIsOpen:currentView:)` pauses when closed and not stats tab.
- **OS deps:** IOKit (IOAccelerator PerformanceStatistics for GPU), IOKit.ps (battery), SMC/IOReport (temp/frequency), Darwin sysctl (CPU).
- **Data flow:** Timer tick → collect metrics → publish @Published → Stats UI graphs update.
- **State:** current metric values, rolling history buffers, monitoring enabled/disabled.
- **Permissions:** none (local counters); temperature may need admin/kernel access.
- **Windows strategy:** PDH `PerformanceCounter` (Processor % Processor Time _Total + per-core, Memory, PhysicalDisk, Network Interface), `GlobalMemoryStatusEx` (memory), WMI/DXGI or vendor libraries (NVML/ADL) for GPU, WMI `MSAcpi_ThermalZoneTemperature` for CPU temp (limited hardware exposure), `Win32_Processor.CurrentClockSpeed` for frequency.
- **Tests:** counter read accuracy, per-core enumeration, polling start/stop, interval configurability, no fabricated values (zero when counter unavailable).

---

## 4. Live Activities

- **Description:** Transient indicators in the closed notch: media, timer, reminder, screen recording, privacy (cam/mic), DND/focus, battery/charging, Bluetooth audio, downloads, LocalSend, extension payloads.
- **Original source:** `components/Live activities/`, `managers/BatteryActivityManager.swift`, `managers/ReminderLiveActivityManager.swift`, `managers/ScreenRecordingManager.swift`, `managers/PrivacyIndicatorManager.swift`, `managers/CameraMonitor.swift`, `managers/MicrophoneMonitor.swift`, `managers/DoNotDisturbManager.swift`, `managers/BluetoothAudioManager.swift`, `managers/DownloadManager.swift`, `managers/LocalSend*.swift`, `managers/ExtensionLiveActivityManager.swift`.
- **Key classes/structs:** `SneakContentType` (timer, reminder, recording, doNotDisturb, bluetoothAudio, privacy, lockScreen, capsLock, extensionLiveActivity), `ReminderLiveActivityManager.ReminderEntry`, `BatteryPowerReading`, `BluetoothAudioDevice`.
- **UI components:** the live-activity views listed under Main Interface, `SystemEventIndicatorModifier`, `BatteryTemporaryActivityView`.
- **Interactions:** dismiss HUD, click-through to full view, configure HUD style/duration (chargingBatteryHUDDuration, lowBatteryHUDDuration, fullBatteryHUDThreshold).
- **Background:** event subscriptions (battery state change, DND toggle, screen recording start/stop, camera/mic access, device arrival/removal, download started), auto-hide timers (battery 3s default, timer 10s).
- **OS deps:** IOKit.ps (battery), screen capture permissions, camera/mic accessibility, FSEvents (downloads), LocalSend LAN.
- **Data flow:** system event → manager sets isActive/isRecording → ContentView shows corresponding LiveActivity → auto-hide Task cancels/shows false.
- **State:** per-activity active flag, HUD token, auto-hide Task.
- **Permissions:** Screen Recording, Camera, Microphone, Accessibility.
- **Windows strategy:** `Windows.System.Power.Battery` for battery; `Windows.Devices.Bluetooth` + `DeviceWatcher` for Bluetooth; WinRT Toast/notifications + collapsed-window HUD for timer/reminder/download; camera/mic and screen-recording detection have no direct Win32 equivalent → documented as partial/platform limitation with heuristic (process/handle enumeration).
- **Tests:** battery level/charging state, Bluetooth device add/remove, timer HUD shows and auto-hides, recording indicator toggles.

---

## 5. Productivity Utilities

### 5.5.1 Timer
- Source: `managers/TimerManager.swift`, `managers/SystemTimerBridge.swift`, `models/TimerPreset.swift`, `enums/TimerDisplayMode.swift`, `components/Timer/`, `managers/LockScreenTimerWidgetManager.swift`.
- Model: `TimerManager` (isTimerActive, activeSource, timerName, timerColor, formattedRemainingTime), `TimerPreset`, `TimerDisplayMode` (.popover etc.).
- Windows: `System.Threading.Timer`, monotonic time adjusted for sleep/resume (`GetTickCount64`), WinUI picker + presets, collapsed HUD during run.

### 5.5.2 Clipboard History
- Source: `managers/ClipboardManager.swift` (789 lines), `components/Clipboard/`, `ClipboardWindowManager.swift`, `ClipboardPanelManager.swift`, models clipboard structs.
- Model: `ClipboardManager` history stack, `persistClipboardHistory`, `clipboardHistorySize`, `clipboardDisplayMode`, `copyOnDrag`, `autoRemoveShelfItems`.
- Interactions: capture on `ContentChanged`, dedupe, filter, paste item, drag-out item, clear, toggle persistence.
- Windows: `Clipboard.ContentChanged` event, `GetData` for formats (text/html/filelist/bitmap), local JSON storage, `IDataObject` drag-out.

### 5.5.3 Color Picker
- Source: `components/ColorPicker/`, `managers/ColorPickerManager.swift` (and _Fixed), `models/PickedColor.swift`.
- Model: `PickedColor` (hex, RGB, HSL, formats), `colorHistorySize`, `colorExtractionMode`, `colorPickerDisplayMode`.
- Windows: `GraphicsCapturePicker`/`ScreenCapture` to sample a pixel, format conversion (hex/RGB/HSL), local history.

### 5.5.4 File Shelf
- Source: `components/Shelf/` (Views, ViewModels, Services, Models), `models/ShelfItem.swift`, `Bookmark.swift`, `drop.swift`, `managers/Shelf*`.
- Model: `ShelfItem` (file URL, bookmark data, title, icon), `ShelfStateViewModel`, `dynamicShelf`, `autoRemoveShelfItems`, `openShelfByDefault`.
- Interactions: add file (drag-in or `open -a Atoll /path`), select, context menu, drag-out, reorder, remove.
- Windows: `IDataObject` drag-drop, Shell `.lnk` shortcuts / shell item ID lists for persistence, context menu (`ContextMenuStrip`/`MenuFlyout`), drag-out `DoDragDrop`.

### 5.5.5 Notes
- Source: `savedNotes`, `enableNotes`, `Components/Notes/`, `managers/AppleNotesSyncManager.swift`.
- Model: note text, `enableNotePinning`, `enableNoteColorFiltering`, `enableNoteSearch`, `enableNoteCharCount`, `savedNotes`.
- Windows: local JSON storage, WinUI editor, pin/color/search.

### 5.5.6 Calendar
- Source: `components/Calendar/`, `managers/CalendarManager.swift`, `models/CalendarModel.swift`, `EventModel.swift`, `CalendarServiceProviding.swift`.
- Model: `EventModel` (title, date, calendar color), lookahead window, all-day/hidden options.
- Windows: no direct Win32 calendar API → parse local `.ics` or use WinRT `AppointmentStore` where available; documented gap.

### 5.5.7 Terminal
- Source: `managers/TerminalManager.swift`, `SwiftTerm` dependency, `terminalFontFamily`, `terminalOpacity`, `terminalStickyMode`, `terminalMaxHeightFraction`.
- Model: `TerminalManager` (pty session), sticky-mode outside-click monitor.
- Windows: Win32 ConPTY + terminal rendering control (complex; evaluate complexity before committing).

### 5.5.8 Screen Assistant / LLM usage
- Source: `managers/ScreenAssistantManager.swift`, `components/ScreenAssistant/`, `managers/LLMUsage/` (providers, quota clients, pricing, JSONL parser).
- Windows: screen capture + provider API calls; local log-path parsing for usage where files exist on Windows.

---

## 6. Bluetooth

- Source: `managers/BluetoothAudioManager.swift` (2542 lines), components Bluetooth, `BluetoothAudioDevice`, `useBluetoothHUD3DIcon`, `showBluetoothDeviceConnections`, `showBluetoothDeviceNameMarquee`.
- Windows: `Windows.Devices.Bluetooth` `BluetoothLEDevice` + `DeviceWatcher`, GATT Battery Service for battery levels, WASAPI `IMMDeviceEnumerator` for audio endpoints.
- Platform note: classic (non-LE) Bluetooth device battery reporting is inconsistent on Windows; document behavior.

---

## 7. Settings & Customization (~38 sections / 374 keys)

- Source: `models/Constants.swift` (`extension Defaults.Keys`), `components/Settings/` (14 files ~11.9k lines), `components/Onboarding/`.
- General: updateChannel, logLevel, menubarIcon, showOnAllDisplays, releaseName.
- Behavior: minimumHoverDuration, enableHaptics, openNotchOnHover, extendHoverArea, externalDisplayStyle, hideNonNotchUntilHover, notchHeightMode/nonNotchHeightMode, notchHeight/nonNotchHeight/openNotchWidth/closedNotchWidth.
- Appearance: showEmojis, showMirror, mirrorShape, selectedCameraID, settingsIconInNotch, lightingEffect, accentColor, enableShadow, ...
- Gestures: enableGestures, gestureSensitivity, enableHorizontalMusicGestures, reverseSwipeGestures, reverseScrollGestures, closeGestureEnabled.
- Media playback (~74 keys): mediaController, musicAuxLeft/RightControl, musicControlSlots, musicGestureBehavior, musicSkipBehavior, enableLyrics, lyricHighlightStyle, useMusicVisualizer, visualizerBarCount, coloredSpectrogram, enableRealTimeWaveform, playerColorTinting, ...
- Battery (~22): showBatteryIndicator, showBatteryPercentage, lowBatteryHUDDuration/Style/Threshold, fullBatteryHUD*, chargingBatteryHUDDuration, lockScreenBattery*.
- Downloads, HUD (~6), Shelf (~10), Calendar (~3), Fullscreen Media Detection, Wobble Animation, Media Controller (~10), Bluetooth Audio Devices (~9), Stats Feature (~17), Terminal Feature (~15), Timer Feature (~12), Reminder Live Activity (~5), Per-App Volume (~3), Caffeinate (~4), ColorPicker (~5), Clipboard (~5), Screen Assistant (~10), Third-Party Extensions (~14), Keyboard Shortcuts, System HUD (~10), Custom OSD Window (~17), Third-Party DDC (~14), Screen Recording Detection (~5), Focus/DND (~5), Privacy Indicators (~2), Lock Screen (~3), Caps Lock (~6), ImageService, Minimalistic UI Mode, Lyrics (~7), Notes (~10).
- Every Windows setting must map to one of these keys or document why not.
- Persistence: `IsolatedStorage`/LocalSettings + JSON, mirrored from Swift `Defaults`/`UserDefaults`.

---

## 8. Background & Lifecycle

- Launch sequence (DynamicIslandApp.applicationDidFinishLaunching): post distributed notification, configure lock-screen + weather managers, start extension XPC + RPC servers, run setting migrations, apply app icon, install menu bar, set up SystemHUD/BetterDisplay/Lunar, purge clipboard history, start screen-recording/DND/privacy/network monitoring, optional audio-tap capture, start memory monitoring, register shortcuts.
- Sleep/lock: `onScreenLocked`/`onScreenUnlocked` → hide/restore windows; `hideDynamicIslandFromScreenCapture`.
- Display change: `screenConfigurationDidChange` → `adjustWindowPosition`; `automaticallySwitchDisplay`.
- Termination: `applicationWillTerminate` → cleanup windows.
- Single-instance: distributed notification `didBecomeActive`.
- Windows equivalent: `Application` lifetime + `WM_WTSSESSION_CHANGE` + `WM_DISPLAYCHANGE` + named mutex.

---

## 9. Assets & Localization

- Icons: AppIcon (stable/Alpha/Beta/Dev/Nightly), player logos (Apple Music, Spotify, Cider, Amazon, Tidal, YouTube Music), bolt, chrome, desktop, plug, sparkle, spotlight, LinkedIn, Github, ebullioscopic, glassdesktop, LocalSend.
- Animations (Lottie JSON): Dog waiting, FingerprintScan, Lock, Moody Dog, Orange Cat Peeping, Reindeer.
- Audio: audio-feedback.m4a, dynamic.m4a, lock.mp3, lowbattery.mp3, timer.mp3, unlock.mp3.
- Localization: `Localizable.xcstrings`, 1603 keys, 19 languages (ar, cs, de, en, en-GB, es, fr, hu, it, ko, nl, pl, pt-BR, ru, th, tr, uk, zh-Hans, zh-Hant).
- Windows: player logos reproduced as PNG where licensing permits; Lottie via Lottie-Windows; .resw resources for 19 languages; sounds via `MediaPlayer`/`SoundPlayer`.
