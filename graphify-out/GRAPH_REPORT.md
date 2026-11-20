# Graph Report - src  (2026-11-19)

## Corpus Check
- Corpus is ~30,781 words - fits in a single context window. You may not need a graph.

## Summary
- 1127 nodes · 2355 edges · 45 communities (41 shown, 4 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 158 edges (avg confidence: 0.85)
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- App Composition & Contracts
- Keyboard Key Enum
- Win32 Interop & Tray
- Clipboard Item Model
- Settings Row Editors
- Notch Window & Spring Easing
- Window Events & Notch VM
- Clipboard Service API
- Media Playback Model
- Logging
- Timer Service API
- Core Enums
- Media Service
- Clipboard Tab UI
- Media Value Converters
- Main Window
- Settings Window Chrome
- Shell View Model
- Home Tab (Media) UI
- Settings Service (JSON)
- Project Files & Dependencies
- Activity & Bluetooth Contracts
- Clipboard Card Control
- Hotkey Registration
- System Metrics Snapshot
- App Bootstrap
- Color Picker Service
- Clipboard Card Layout
- Notch View Tabs
- App Startup Wiring
- System Metrics Service
- GPU Device Metrics
- Shortcut Model
- Timer Tab UI
- Settings Window Logic
- Media Transport Handlers
- Activity & Session Events
- Clipboard Item Types
- Power Service
- Modifier Keys Enum
- Timer State Model
- Stats Tab UI
- Bluetooth Device Info
- Shelf Item Model

## God Nodes (most connected - your core abstractions)
1. `Key` - 74 edges
2. `ClipboardService` - 54 edges
3. `ShellViewModel` - 52 edges
4. `NotchWindow` - 47 edges
5. `ClipboardItem` - 40 edges
6. `Atoll.Core.Contracts` - 37 edges
7. `MediaPlaybackState` - 37 edges
8. `TrayService` - 30 edges
9. `MediaService` - 28 edges
10. `SystemMetricsSnapshot` - 26 edges

## Surprising Connections (you probably didn't know these)
- `State` --calls--> `TimerState`  [INFERRED]
  Atoll.Services/Productivity/TimerService.cs → Atoll.Core/Contracts/ITimerService.cs
- `App` --references--> `SettingsWindow`  [EXTRACTED]
  Atoll.App/App.xaml.cs → Atoll.UI/Settings/SettingsWindow.xaml.cs
- `ClipboardCardControl` --references--> `IClipboardService`  [EXTRACTED]
  Atoll.Platform.Windows/Windowing/ClipboardCardControl.xaml.cs → Atoll.Core/Contracts/IClipboardService.cs
- `ClipboardTabContent` --references--> `IClipboardService`  [EXTRACTED]
  Atoll.Platform.Windows/Windowing/ClipboardTabContent.xaml.cs → Atoll.Core/Contracts/IClipboardService.cs
- `ClipboardService` --implements--> `IClipboardService`  [EXTRACTED]
  Atoll.Services/Productivity/Clipboard/ClipboardService.cs → Atoll.Core/Contracts/IClipboardService.cs

## Import Cycles
- None detected.

## Communities (45 total, 4 thin omitted)

### Community 0 - "App Composition & Contracts"
Cohesion: 0.05
Nodes (21): ISessionService, IsSessionLocked, DesignTokens, Atoll.Platform.Windows.Input, Atoll.Platform.Windows.Shell, Atoll.App, Atoll.UI.Settings, Atoll.Core.Infrastructure (+13 more)

### Community 1 - "Keyboard Key Enum"
Cohesion: 0.03
Nodes (70): Key, A, B, Back, C, D, D0, D1 (+62 more)

### Community 2 - "Win32 Interop & Tray"
Cohesion: 0.07
Nodes (17): Rect, Height, Width, CursorPosition, POINT, MonitorHelper, NativeMethods, MonitorEnumProc (+9 more)

### Community 3 - "Clipboard Item Model"
Cohesion: 0.07
Nodes (17): ClipboardItem, FilePaths, Html, Id, ImageBytes, ImageFileName, Preview, Rtf (+9 more)

### Community 4 - "Settings Row Editors"
Cohesion: 0.05
Nodes (28): BoolEditor, ColorSwatch, EnumEditor, KeyLabel, MetaLabel, TextEditor, Thumb, Track (+20 more)

### Community 5 - "Notch Window & Spring Easing"
Cohesion: 0.06
Nodes (18): SpringEasing, DampingFraction, SpringMath, ContentHost, NotchBg, Surface, Window, NotchWindow (+10 more)

### Community 6 - "Window Events & Notch VM"
Cohesion: 0.05
Nodes (38): ActivityHiddenEventArgs, Type, ActivityShownEventArgs, Payload, Type, ClipboardClearedEventArgs, PersistDisabled, ShortcutInvokedEventArgs (+30 more)

### Community 7 - "Clipboard Service API"
Cohesion: 0.09
Nodes (9): IClipboardService, History, IsMonitoring, MaxHistorySize, PersistHistory, INotificationService, IShelfService, DynamicShelf (+1 more)

### Community 8 - "Media Playback Model"
Cohesion: 0.05
Nodes (43): MediaCommand, Like, Next, Pause, Play, PlayPause, Previous, SeekBackward (+35 more)

### Community 9 - "Logging"
Cohesion: 0.10
Nodes (13): ILogger, LogLevel, Debug, Error, Info, None, Warn, ConsoleLogger (+5 more)

### Community 10 - "Timer Service API"
Cohesion: 0.11
Nodes (11): ITimerService, Presets, State, TimerPreset, Color, Duration, Id, Name (+3 more)

### Community 11 - "Core Enums"
Cohesion: 0.06
Nodes (31): MediaSkipBehavior, PlusMinusTenSeconds, Track, NotchState, Closed, Open, SneakContentType, Backlight (+23 more)

### Community 12 - "Media Service"
Cohesion: 0.12
Nodes (6): IMediaService, CurrentState, IsMonitoring, MediaService, CurrentState, IsMonitoring

### Community 13 - "Clipboard Tab UI"
Cohesion: 0.09
Nodes (14): Bd, CardsPanel, ClearButton, ClearOverlay, EmptyState, HistoryScroller, UserControl, ClipboardTabContent (+6 more)

### Community 14 - "Media Value Converters"
Cohesion: 0.19
Nodes (6): ArtworkConverter, BoolToAccentBrushConverter, IsNullToVisibilityConverter, PlayPauseGlyphConverter, RepeatBrushConverter, RepeatGlyphConverter

### Community 15 - "Main Window"
Cohesion: 0.10
Nodes (17): Window, MainWindow, Shell, MediaState.Artist, MediaState.IsPlaying, MediaState.Title, MetricsSnapshot.CpuTotalPercent, MetricsSnapshot.MemoryUsedPercent (+9 more)

### Community 16 - "Settings Window Chrome"
Cohesion: 0.10
Nodes (21): Bd, PART_Popup, PART_Track, SectionList, SectionSubtitle, SectionTitle, SettingsList, T (+13 more)

### Community 17 - "Shell View Model"
Cohesion: 0.14
Nodes (11): ShellViewModel, ClipboardService, CurrentView, IsClipboardDragInProgress, MediaState, MetricsSnapshot, Notches, Tabs (+3 more)

### Community 18 - "Home Tab (Media) UI"
Cohesion: 0.10
Nodes (20): Bd, ProgressTrack, UserControl, ActualWidth, MediaState.Artist, MediaState.IsPlaying, MediaState.Title, Border (+12 more)

### Community 19 - "Settings Service (JSON)"
Cohesion: 0.15
Nodes (5): SettingChangedEventArgs, Key, NewValue, OldValue, JsonSettingsService

### Community 20 - "Project Files & Dependencies"
Cohesion: 0.14
Nodes (14): net10.0-windows10.0.19041.0, Microsoft.Extensions.DependencyInjection (11.0.0), Microsoft.NET.Sdk, Microsoft.NET.Sdk, net10.0-windows10.0.19041.0, Microsoft.NET.Sdk, net10.0-windows10.0.19041.0, Microsoft.Extensions.DependencyInjection (11.0.0) (+6 more)

### Community 21 - "Activity & Bluetooth Contracts"
Cohesion: 0.13
Nodes (11): IActivityService, Active, IBluetoothService, ConnectedDevices, MediaSessionInfo, AppUserModelId, IsActive, IsSource (+3 more)

### Community 23 - "Hotkey Registration"
Cohesion: 0.21
Nodes (3): HotkeyWindow, Handle, ShortcutService

### Community 24 - "System Metrics Snapshot"
Cohesion: 0.12
Nodes (17): SystemMetricsSnapshot, CpuCurrentFrequencyMhz, CpuPerCorePercent, CpuTemperatureCelsius, CpuTotalPercent, DiskBytesReadPerSec, DiskBytesWrittenPerSec, GpuDevices (+9 more)

### Community 25 - "App Bootstrap"
Cohesion: 0.14
Nodes (7): Application, Bd, App, Host, Border, ServiceLocator, Services

### Community 26 - "Color Picker Service"
Cohesion: 0.14
Nodes (13): IColorPickerService, History, MaxHistorySize, PickedColor, B, Format, G, Hex (+5 more)

### Community 27 - "Clipboard Card Layout"
Cohesion: 0.18
Nodes (13): CopiedOverlay, DeleteButton, IconGlyph, PreviewText, Root, ThumbnailHost, TimeText, TypeText (+5 more)

### Community 28 - "Notch View Tabs"
Cohesion: 0.17
Nodes (11): NotchView, Clipboard, ColorPicker, ExtensionExperience, Home, LlmUsage, Notes, Shelf (+3 more)

### Community 29 - "App Startup Wiring"
Cohesion: 0.25
Nodes (6): ISettingsService, IShortcutService, ITrayService, ToolTip, Visible, Settings

### Community 30 - "System Metrics Service"
Cohesion: 0.18
Nodes (5): ISystemMetricsService, EnabledCategories, IsMonitoring, LatestSnapshot, PollInterval

### Community 31 - "GPU Device Metrics"
Cohesion: 0.18
Nodes (11): GpuDeviceMetrics, CoreClockMhz, CoreCount, FanSpeedPercent, Id, IsActive, MemoryClockMhz, Model (+3 more)

### Community 32 - "Shortcut Model"
Cohesion: 0.24
Nodes (6): Shortcut, Description, IsGlobal, Key, Modifiers, Name

### Community 33 - "Timer Tab UI"
Cohesion: 0.27
Nodes (6): UserControl, TimerTabContent, _shell, _timer, TimerState.Name, TimerState.Remaining

### Community 36 - "Activity & Session Events"
Cohesion: 0.25
Nodes (7): ActiveActivity, DurationSeconds, ShownAt, Type, SessionChangedEventArgs, IsLocked, OccurredAt

### Community 37 - "Clipboard Item Types"
Cohesion: 0.29
Nodes (7): ClipboardItemType, File, Image, Rtf, Text, Unknown, Url

### Community 38 - "Power Service"
Cohesion: 0.29
Nodes (7): IPowerService, Current, PowerState, ChargePercent, EstimatedTimeRemaining, IsCharging, IsPresent

### Community 39 - "Modifier Keys Enum"
Cohesion: 0.29
Nodes (6): ModifierKeys, Alt, Control, None, Shift, Windows

### Community 40 - "Timer State Model"
Cohesion: 0.29
Nodes (7): TimerState, Duration, IsActive, IsPaused, Name, Remaining, StartedAt

### Community 41 - "Stats Tab UI"
Cohesion: 0.29
Nodes (4): UserControl, StatsTabContent, MetricsSnapshot.CpuTotalPercent, MetricsSnapshot.MemoryUsedPercent

### Community 42 - "Bluetooth Device Info"
Cohesion: 0.33
Nodes (6): BluetoothDeviceInfo, BatteryPercent, DeviceClass, Id, IsConnected, Name

### Community 43 - "Shelf Item Model"
Cohesion: 0.33
Nodes (6): ShelfItem, AddedAt, DisplayName, FilePath, IconUri, Id

## Knowledge Gaps
- **402 isolated node(s):** `Border`, `Host`, `net10.0-windows10.0.19041.0`, `Microsoft.Extensions.DependencyInjection (11.0.0)`, `Microsoft.Extensions.Hosting (11.0.0)` (+397 more)
  These have ≤1 connection - possible missing edges. (Counts symbols only; 470 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **4 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `ShellViewModel` connect `Shell View Model` to `App Composition & Contracts`, `Settings Row Editors`, `Notch Window & Spring Easing`, `Window Events & Notch VM`, `Clipboard Service API`, `Media Playback Model`, `Logging`, `Timer Service API`, `Media Service`, `Clipboard Tab UI`, `Main Window`, `Activity & Bluetooth Contracts`, `System Metrics Snapshot`, `Notch View Tabs`, `App Startup Wiring`, `System Metrics Service`, `Timer Tab UI`, `Media Transport Handlers`, `Timer State Model`, `Notch Toggle & Shortcuts`?**
  _High betweenness centrality (0.223) - this node is a cross-community bridge._
- **What connects `Border`, `Host`, `net10.0-windows10.0.19041.0` to the rest of the system?**
  _402 weakly-connected nodes found - possible missing edges._
- **Should `App Composition & Contracts` be split into smaller, more focused modules?**
  _Cohesion score 0.05498652291105121 - nodes in this community are weakly interconnected._
- **Why does `NotchWindow` connect `Notch Window & Spring Easing` to `App Composition & Contracts`, `Win32 Interop & Tray`, `Window Events & Notch VM`, `Logging`, `Clipboard Tab UI`, `Main Window`, `Shell View Model`, `Clipboard Card Control`?**
  _High betweenness centrality (0.121) - this node is a cross-community bridge._
- **Should `Keyboard Key Enum` be split into smaller, more focused modules?**
  _Cohesion score 0.02857142857142857 - nodes in this community are weakly interconnected._
- **Why does `Key` connect `Keyboard Key Enum` to `App Composition & Contracts`, `Shortcut Model`, `Hotkey Registration`, `Modifier Keys Enum`?**
  _High betweenness centrality (0.106) - this node is a cross-community bridge._
- **Should `Win32 Interop & Tray` be split into smaller, more focused modules?**
  _Cohesion score 0.06605222734254992 - nodes in this community are weakly interconnected._