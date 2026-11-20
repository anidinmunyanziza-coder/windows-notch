# Atoll for Windows — Testing Strategy

Source of truth: https://github.com/Ebullioscopic/Atoll (commit b52c3a7)

## Test pyramid

| Layer | Tool | Scope |
|---|---|---|
| Unit | xUnit + Moq | Service logic, settings, metric parsing, media model mapping, color conversion, timer math |
| Integration | xUnit (in-process WinUI) | DI container, service startup/shutdown, settings persistence round-trip, window creation |
| UI | WinAppDriver / WinUI Automation | End-to-end user flows: hover open, tab switch, media control, settings change |
| Visual regression | screenshot + pixel diff (ImageMagickSharp / Skia) | Layout, spacing, colors, alignment across resolutions/scales/states |

## Where the original is the reference

- Original unit tests live in `DynamicIslandTests/` (AppleMusicController, AudioTap, CaffeinateManager, CiderFavoriting, ClipboardHistoryPersistence, FlyoutFrameCalculator, JSONLUsageParser, LRCParser, LyricsMetadata, MediaKeyTapRetryPolicy, MenuBarClearance, PerAppVolume, SpotifyLibrary, SystemHUDPlacement, TimerLifecycleRegression, etc.) and `tests/` (Python/Swift regressions). These define expected behavior and porting targets.
- Where the original asserts behavior (e.g. `ClipboardHistoryPersistenceTests`, `TimerLifecycleRegression`), write an equivalent C# test asserting the same invariant on Windows.

## Required test categories

1. **Application startup/shutdown** — app launches, single-instance mutex works, services start in order, shutdown disposes cancellations/timers, settings persist.
2. **Feature initialization** — each service initializes without crashing when its feature toggle is off (graceful degradation).
3. **State management** — notch state transitions (closed→hover→open→closed), tab order/direction, sneak-peek show/auto-hide, expanding view auto-hide.
4. **Settings persistence** — write a setting, restart (simulated), read back; 374-key schema coverage (each Windows-exposed setting maps to `settings-schema.json`).
5. **Media interactions** — GSMTC session enumeration, play/pause/next/prev, property mapping (title/artist/artwork), seek, volume, like (Spotify), no fake data when no session.
6. **System metrics** — counter read returns real values or "unavailable"; per-core enumeration; polling respects interval; monitoring pauses when notch closed (`statsStopWhenNotchCloses` equivalent); never fabricates values.
7. **Clipboard operations** — capture on `ContentChanged`, dedupe, history size limit, persistence round-trip, paste, clear.
8. **Timer functionality** — countdown accuracy, presets, sleep/resume adjustment, auto-HUD show/hide.
9. **File handling** — shelf add/remove/drag-out, `.lnk` persistence, context menu.
10. **Window positioning** — per-monitor creation, top-edge placement, DPI scaling, multi-monitor enumeration, fullscreen hide, lock hide/restore.
11. **Error handling** — missing counter, missing Bluetooth adapter, missing media player, settings file corrupt → graceful fallback, logged error, no crash.
12. **Background service lifecycle** — start/stop on toggle, cancellation tokens disposed, no duplicate subscriptions, no memory leak over long run.

## UI automation

- Use WinAppDriver + WinUI Automation (`AutomationElement`, `TreeWalker`) to:
  - open the notch (click / hover simulation),
  - switch tabs,
  - click media buttons,
  - open settings and toggle a feature,
  - verify collapsed live activity appears when media plays.
- Map original SwiftUI accessibility identifiers where present (`accessibilityIdentifier("AtollNotch")`, etc.) to WinUI `AutomationProperties.AutomationId`.

## Visual regression

- Capture screenshots of the notch in: closed, hover, open-home, open-stats, open-media, with live activity, at:
  - 1920x1080 @ 100%, 125%, 150% scaling
  - 2560x1440, 3840x2160
  - multi-monitor (2 displays, different DPI)
- Diff against reference screenshots (to be captured from the original macOS app where available, else from a first Windows build as baseline).
- Flag: incorrect spacing/sizing/misalignment, color differences, missing elements, wrong corner radius/shadow.
- For animations: capture representative frames during open/close/tab-switch and compare timing + easing; compare `Animation.spring` response/damping to Composition spring parameters.

## Known-difference tracking

- Every test that passes/fails against original behavior is recorded in `PARITY_MATRIX.md` (Fn/Vis columns) and `KNOWN_DIFFERENCES.md`.
- A feature is `FULLY VERIFIED` only when both its functional test and its visual test pass against the original reference.

## CI

- Unit + integration tests run on every build.
- UI tests run on Windows 10 and Windows 11 VMs where available.
- Visual regression runs on PRs that touch UI code.
