# Atoll for Windows — Known Differences vs Original

Source of truth: https://github.com/Ebullioscopic/Atoll (commit b52c3a7, v2.3.3)

This document records features where the Windows port deliberately differs from the macOS original, either because no equivalent Windows API exists or because the underlying platform concept does not exist on Windows. Each difference is classified:

- **UNAVAILABLE** — no Windows API; feature omitted or replaced by closest behavior.
- **PARTIAL** — closest-behavior reproduction; some original detail lost.
- **BEHAVIOR-CHANGED** — same user-facing intent, different Windows mechanism.

---

## 1. Primary interface

| # | Original | Windows | Classification | Closest behavior / notes |
|---|---|---|---|---|
| 1 | macOS "notch" at top-center of MacBook display | Floating top-edge pill on each Windows monitor | UNAVAILABLE | The notch hardware doesn't exist on Windows; reproduced as a resizable floating surface at the top edge of each monitor (`showOnAllDisplays`, `preferred_screen`). |
| 2 | Menu-bar (`NSStatusItem`) | Notification-area tray icon | BEHAVIOR-CHANGED | `Shell_NotifyIcon` tray icon + context menu; opens settings/notch. |
| 3 | Window levels/spaces (`NSWindow.level`, `NSWorkspace` spaces) | Win32 topmost layered windows | PARTIAL | Topmost popup windows; no "spaces" concept on Windows. |
| 4 | Click-through / passive window | `WS_EX_TRANSPARENT` + hit-test handling | PARTIAL | Evaluated per feature. |

## 2. Media

| # | Original | Windows | Classification | Closest behavior / notes |
|---|---|---|---|---|
| 5 | AppleScript automation of Apple Music | Not available | UNAVAILABLE | Replaced by GSMTC (universal) + Spotify Web API. Apple Music-specific AppleScript commands lost unless player exposes GSMTC. |
| 6 | `MediaRemoteAdapter.framework` (native) | Not available | UNAVAILABLE | Native media-remote bridge is macOS-only; GSMTC is the Windows substitute. |
| 7 | Cider API | Not available | PARTIAL | If Cider exposes GSMTC it works universally; otherwise no dedicated Cider adapter. |
| 8 | Like/favorite on Apple Music | Not available via GSMTC | PARTIAL | Spotify like via Spotify Web API; other players only if they expose it. |
| 9 | Animated/video artwork as lock-screen wallpaper | Not available | UNAVAILABLE | macOS-only wallpaper API. Animated artwork can still render inside the media pane (Media Foundation/Win2D). |

## 3. System monitoring

| # | Original | Windows | Classification | Closest behavior / notes |
|---|---|---|---|---|
| 10 | CPU temperature via SMC / IOReport | WMI `MSAcpi_ThermalZoneTemperature` | PARTIAL | Not exposed on all hardware (often desktop); returns "unavailable" rather than fabricating. |
| 11 | CPU frequency via IOReport | `Win32_Processor.CurrentClockSpeed` | PARTIAL | May report 0 or current vs max depending on hardware. |
| 12 | GPU via `IOAccelerator PerformanceStatistics` | WMI/DXGI + vendor libs (NVML/ADL optional) | PARTIAL | Discrete-GPU telemetry varies widely by vendor; integrated GPU often limited. |
| 13 | Memory "wired/active/inactive/compressed" | Not available (macOS VM stats) | BEHAVIOR-CHANGED | Report total/used/available/commit; compressed/wired breakdown dropped. |
| 14 | Memory pressure level | Approximated from available % | PARTIAL | Normal/warning/critical thresholds approximated. |

## 4. Privacy & security indicators

| # | Original | Windows | Classification | Closest behavior / notes |
|---|---|---|---|---|
| 15 | Camera/mic access monitoring (`CameraMonitor`/`MicrophoneMonitor`) | No direct Windows API | UNAVAILABLE | Heuristic only (process/handle enumeration) — not shipped by default; documented gap. |
| 16 | Screen-recording detection (`CGPreflightScreenCaptureAccess`) | No direct equivalent | UNAVAILABLE | Heuristic only; documented gap. |
| 17 | Focus Assist / Do Not Disturb state | Not readable by third parties | UNAVAILABLE | Documented gap; DND live activity shown only if state can be inferred (not natively). |

## 5. Lock screen

| # | Original | Windows | Classification | Closest behavior / notes |
|---|---|---|---|---|
| 18 | Lock-screen widgets (`SkyLightWindow`, `LockScreen*Manager`) | Not supported | UNAVAILABLE | Windows lock screen is not extensible by third-party apps. Lock-screen widget settings remain in UI but are non-functional; documented. |

## 6. Productivity

| # | Original | Windows | Classification | Closest behavior / notes |
|---|---|---|---|---|
| 19 | Calendar (EventKit + third-party Fantastical) | No direct Win32 calendar API | PARTIAL | Local `.ics` parsing or WinRT `AppointmentStore` (limited); may not match EventKit coverage. |
| 20 | Terminal (`SwiftTerm` xterm emulation) | Win32 ConPTY + terminal control | PARTIAL | High complexity to port; evaluate before committing. May be deferred. |
| 21 | Screen Assistant (AI screen capture) | Requires screen capture + LLM provider | PARTIAL | Depends on screen-capture permission and external LLM API; evaluate. |
| 22 | Apple Notes sync | Not available | UNAVAILABLE | Notes stored locally only; no macOS Notes sync. |
| 23 | LocalSend | LAN discovery via sockets | PARTIAL | Windows UDP/TCP discovery possible; parity depends on protocol implementation. |

## 7. Extensions & system integration

| # | Original | Windows | Classification | Closest behavior / notes |
|---|---|---|---|---|
| 24 | Third-party extensions (`AtollExtensionKit`, XPC service, RPC server) | Not available | UNAVAILABLE | Out-of-process .NET extensibility is future work; extension tab hidden by default. |
| 25 | Accessibility permissions (global event monitoring) | UIPI / manifest + admin where needed | BEHAVIOR-CHANGED | Global hooks/hotkeys need appropriate manifest capabilities; some global events require elevated integrity. |
| 26 | Sparkle updates (`SUFeedURL` appcast + ED key) | GitHub Releases API | BEHAVIOR-CHANGED | Simpler update check; signature verification replaced by HTTPS + publisher certificate. |

## 8. Assets & media

| # | Original | Windows | Classification | Closest behavior / notes |
|---|---|---|---|---|
| 27 | Lottie "FingerprintScan" animation | Lottie Simple License | PARTIAL | Reusable if license complied with; attribution retained. |
| 28 | App icon variants (Alpha/Beta/Dev/Nightly) | Single Windows icon | BEHAVIOR-CHANGED | Windows has no "channel icon" concept; channel shown in about/settings instead. |
| 29 | System sounds (.m4a/.mp3) | WAV/MP3 via `MediaPlayer` | BEHAVIOR-CHANGED | Repackage; verify redistribution rights for each sound. |

## Licensing notes

- Atoll is GPL-3.0. Any code/assets adapted from the original retain GPL-3.0 and require source availability.
- Third-party assets (Lottie animations by Eddy Gann, player logos) retain their own licenses — see `COPYRIGHT_ASSETS`, `NOTICE`, `TRADEMARKS` in the original repo.
- "Dynamic Island" and "Atoll" are trademarks of their owners; the port is not affiliated with Apple.

## Process rule

A difference is added here only after the original implementation has been read and the Windows API surface checked. Do not mark "unavailable" without evidence. Update `PARITY_MATRIX.md` (`PLATFORM LIMITATION` status) whenever a new difference is recorded.
