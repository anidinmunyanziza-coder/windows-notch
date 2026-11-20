# Atoll for Windows — Reverse-Engineering Notes

Source of truth: https://github.com/Ebullioscopic/Atoll (commit b52c3a7, v2.3.3)
Investigation location: `C:\Users\Anidin\projects\atoll-windows\original`

## Investigation method

1. Cloned repo (depth 50) to `...\original`.
2. Enumerated entire tree: 391 Swift files, 73 PNG, 6 SVG, 6 MOV, 4 MP3, 2 M4A, 1 GIF, 6 Lottie JSON, 1 xcstrings (1603 keys / 19 languages), entitlements, Info.plist, Xcode project, SwiftPM dependencies.
3. Extracted the complete Defaults settings schema (374 keys / 38 sections) from `models/Constants.swift` → `docs/notes/settings-schema.{json,md}`.
4. Indexed all Swift symbols (1132 lines) → `docs/notes/swift-symbol-index.txt`; ContentView structs → `docs/notes/contentview-structs.txt`.
5. Read core files directly: `DynamicIslandApp.swift` (lifecycle), `DynamicIslandViewCoordinator.swift` (state), `ContentView.swift` (UI hierarchy), `StatsManager.swift` (metrics model), `MusicManager.swift` (media model), `Info.plist`/`entitlements` (permissions/deps).
6. Verified build toolchain: git 2.56, .NET 10 SDK 10.0.404, WPF build confirmed, WinUI 3 targeted per architecture; GSMTC API probe compiled & ran (0 sessions, no players running — expected).
7. Subagent-based parallel analysis was attempted but hit usage limits; remaining analysis done directly via shell/read/grep.

## Key architectural findings

### App entry & lifecycle
- `DynamicIslandApp.DynamicNotchApp: App` + `AppDelegate: NSObject, NSApplicationDelegate`.
- `applicationDidFinishLaunching` starts (in order): lock-screen/weather window managers, extension XPC + RPC server, setting migrations, app-icon apply, menu-bar install, SystemHUD/BetterDisplay/Lunar setup, clipboard history purge, screen-recording/DND/privacy/network monitoring start (gated on defaults + `!isUITesting`), optional audio-tap capture, memory monitoring start, shortcut registration.
- `applicationWillTerminate` cleans up windows.
- Screen lock/unlock (`onScreenLocked`/`onScreenUnlocked`) → `hideWindowsForLock`/`restoreWindowsAfterLock`.
- Display change (`screenConfigurationDidChange`) → `adjustWindowPosition`; `automaticallySwitchDisplay`.
- Single-instance via distributed notification `AtollDistributedNotifications.didBecomeActive`.

### State & coordination
- `DynamicIslandViewCoordinator` (singleton): `currentView` (NotchViews tab), `tabOrder`, `tabSwitchForward`, `sneakPeek` (show/type/value/icon/title/subtitle/accentColor/targetScreen), `expandingView`, `sneakPeekTask`, `expandingViewTask`, `toggleSneakPeek`, `toggleExpandingView`, `suppressHoverOpen`, `handleStatsTabTransition`, `handleMinimalisticModeChange`.
- `DynamicIslandViewModel` (vm): `notchState` (closed/open), `closedNotchSize`, `effectiveClosedNotchHeight`, `hideOnClosed`, `isStatsPopoverActive`, `isBatteryPopoverActive`, `shouldRecheckHover`, `onViewTeardown`.
- `@AppStorage` / `@Default` settings drive behavior (374 keys).

### UI structure (ContentView.swift)
- `NotchLayout()`: closed-state live-activity cascade (battery HUD → inline HUD → caps lock → music → timer → reminder → recording → download → LocalSend → DND → privacy → extension → shelf → face animation → header when open), then open-state `ZStack` tab switcher.
- Tab views: `.home → NotchHomeView`, `.shelf → NotchShelfView`, `.timer → NotchTimerView`, `.stats → NotchStatsView`, `.llmUsage`, `.colorPicker`, `.notes`, `.clipboard`, `.terminal`, `.extensionExperience`.
- Tab switch transition: `tabSwitchTransition` (`blurReplace.animation(.interactiveSpring(dampingFraction: 1.2))`); direction tracked by `tabSwitchForward`.
- Closed-notch sizing: `closedNotchWidth` (default 150), `notchHeight` (32); open width `openNotchWidth` (640); timer adds 250 height; notes/clipboard/terminal have dynamic heights.
- Hover: `onHover { handleHover }`; open on tap; gestures down/left/right/up; haptics on tap (`enableHaptics`); shadow radius 5 closed / 10 open (`enableShadow`); `.smooth(duration: 0.25)` on offset, `.smooth(duration: 0.3)` on view change.
- Context menu right-click → `SettingsWindowController.shared.showWindow()`.

### Media model
- `MusicManager` (ObservableObject, shared): `songTitle`, `artistName`, `albumArt` (NSImage), `albumArtGlowTexture`, `isPlaying`, `album`, `isPlayerIdle`, `isCurrentTrackExplicit`, `avgColor`, `secondaryColor`, `bundleIdentifier`, `songDuration`, `elapsedTime`, `timestampDate`, `playbackRate`, `isShuffled`, `repeatMode` (.off), `isLiveStream`, `isAdvertisement`, `usingAppIconForArtwork`, `skipGesturePulse`, `currentLyrics`, `syncedLyrics:[LyricLine]`, `lyricsAvailability`, `showLyrics`, `currentLyricIndex`, `isCurrentTrackLiked` (Bool?), `canFavoriteCurrentTrack`, `videoArtworkURL`, `flipAngle`, `lastFlipDirection`, `isTransitioning`.
- Optimistic playback transition (deadline/generation), manual-track-artwork handoff (0.225s), iTunes explicitness lookup (URLSession, 300-entry cache).
- `MediaKeyInterceptor` for global media keys; `SystemMediaControllers` is the macOS native equivalent of GSMTC.

### Stats model
- `MemoryBreakdown` (total/used/free/wired/active/inactive/compressed/app/cache bytes + swap + pressure, `usedPercentage` etc.).
- `CPUCoreUsage(id, usage)`.
- `GPUDeviceMetrics` (vendor/model/utilization/renderUtilization/tilerUtilization/temperature/fanSpeed/coreClock/memoryClock/cores/isActive) collected via IOKit `IOAccelerator` `PerformanceStatistics`.
- `StatsManager.updateMonitoringState(notchIsOpen:currentView:)` pauses polling when notch closed & not stats tab (`statsStopWhenNotchCloses`).

### Sneak-peek / live-activity types
`SneakContentType`: brightness, volume, backlight, music, mic, battery, download, timer (duration forced 10s), reminder, recording, doNotDisturb, bluetoothAudio, privacy, lockScreen, capsLock, extensionLiveActivity. `isExtensionPayload` distinguishes third-party.

### Defaults of note for Windows port
- `minimumHoverDuration`=0.3, `openNotchWidth`=640, `closedNotchWidth`=150, `notchHeight`=32, `nonNotchHeight`=32, `enableShadow`=true, `accentColor`=blue, `enableHaptics`=true, `openNotchOnHover`=true, `showOnAllDisplays`=false, `automaticallySwitchDisplay`=true, `statsUpdateInterval`, `enableStatsFeature`, `showCpu/Memory/Gpu/Network/DiskGraph`, `clipboardHistorySize`, `persistClipboardHistory`, `timerPresets`, `timerDisplayMode`, `enableTimerFeature`, etc.

## Files of highest porting priority

1. `ContentView.swift` — UI layout & interaction model (reference for WinUI shell).
2. `DynamicIslandViewCoordinator.swift` — state machine (reference for shell view model).
3. `DynamicIslandApp.swift` — lifecycle & service startup order (reference for `Atoll.App`).
4. `MusicManager.swift` — media state model (reference for `IMediaService`).
5. `StatsManager.swift` — metrics model (reference for `ISystemMetricsService`).
6. `ClipboardManager.swift` — clipboard capture/persistence (reference for `IClipboardService`).
7. `TimerManager.swift` — timer model (reference for `ITimerService`).
8. `models/Constants.swift` — settings schema (reference for settings pages).
9. `BluetoothAudioManager.swift` — Bluetooth model (reference for `IBluetoothService`).
10. `managers/SystemMediaControllers.swift` — native media controller pattern.

## Platform-specific risks already identified

- No Windows equivalent for: macOS notch, menu bar, SkyLightWindow lock-screen widgets, EventKit calendar, CGPreflightScreenCaptureAccess, camera/mic access monitoring, Focus Assist state, AppleScript/automation, MediaRemoteAdapter.framework, SwiftTerm terminal, AtollExtensionKit XPC/RPC.
- These are logged in `KNOWN_DIFFERENCES.md` with closest-behavior replacements.

## Verified on this machine (build/probe)

- `dotnet --list-sdks`: 10.0.404. `dotnet new wpf` builds clean (0 warnings/errors). WinUI 3 targeted per architecture (Windows App SDK not installed; to add, install `Microsoft.WindowsAppSDK` workload or use WinUI 3 VSIX).
- GSMTC probe (`Windows.Media.Control`) compiled and ran: `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` works; 0 sessions because no media player was running.
- Performance counters accessible: CPU `% Processor Time _Total`, disk, network categories available (verified in probe plan; full validation pending).

## Next steps (Phase 2 → Phase 3)

1. Original UI/UX investigation: review Lottie animations + PNG assets, read screenshots/README for motion; finalize design tokens.
2. Create `Atoll.Windows` solution, `Atoll.Core` contracts + `Atoll.App` entry, DI, logging, settings.
3. Implement `WindowService` (per-monitor floating windows), `ShellViewModel` (notch state + tabs), `SettingsService`.
4. Implement features in priority order: media, stats, clipboard, timer, then remaining.
5. Update `PARITY_MATRIX.md` statuses as each feature is IMPLEMENTED → FUNCTIONALLY/VISUALLY VERIFIED.
