# Atoll for Windows

A Dynamic Island for your PC, made by **f4hz_**. **Atoll for Windows** was inspired by
[Atoll](https://github.com/Ebullioscopic/Atoll), the macOS notch app. It adds a
small black notch to the top of your screen that shows what's happening at a
glance and springs open into a full control centre when you hover it.

> Built with WPF on .NET 10. Windows 10 (2004) or Windows 11, 64-bit.

## Features

### The notch
- A pill that rests at the top of the screen and **springs open on hover**, using the original's spring curves and shape
- **Matched-geometry morphs**: album art and the waveform fly between the closed pill and the open player
- **Gestures**: two-finger swipe down to open, up to close, left/right to switch tabs (or skip tracks on the closed music pill)
- **Hides automatically** while the app playing your media is fullscreen
- Works on every monitor

### Live activities on the closed pill
- **Now playing**: artwork and an album-tinted waveform
- **Live lyrics**: synced lines in a capsule that splits off the notch while music plays and merges back when it stops
- **Volume and brightness HUD** with a colour-coded level bar
- **Caps lock**, **camera / microphone in use**, and **Bluetooth headphones connected** (with battery)
- **Charging** and **timer** activities
- A little **idle face** when nothing else is going on

### Tabs in the open notch
| Tab | What it does |
|---|---|
| **Home** | Apple-style Now Playing: artwork, drag-to-seek scrubber, shuffle / repeat |
| **Calendar** | Upcoming events |
| **Shelf** | Drop files in, drag them back out anywhere, share via Nearby Share |
| **Stats** | CPU and memory graphs |
| **Timer** | Countdown timers with presets |
| **LLM Usage** | Claude Code and Codex token usage, estimated cost and rate-limit gauges, read from local logs |
| **Notes** | Quick notes with colours, pinning, image paste and autosave |
| **Clipboard** | Clipboard history with drag-out |

Plus a screen **colour picker** and **settings** button in the top strip, next to an iOS-style battery.

## Install

1. Download the latest `Atoll-Windows-*-win-x64.zip` from [Releases](../../releases).
2. Unzip it anywhere and run `Atoll.App.exe`. Keep the `Settings` folder next to it.

It's self-contained, so you don't need .NET installed. The app isn't code-signed, so
Windows SmartScreen may warn you the first time: click **More info → Run anyway**.

Atoll lives in the notification area (system tray). Right-click the tray icon for
Settings or to quit.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone --recurse-submodules https://github.com/anidinmunyanziza-coder/windows-notch.git
cd windows-notch
dotnet run --project src/Atoll.App
```

Run the tests:

```powershell
dotnet test tests/Atoll.UnitTests
```

Make a release build (a single self-contained `.exe`):

```powershell
dotnet publish src/Atoll.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

### Project layout

| Folder | Contents |
|---|---|
| `src/Atoll.App` | Startup, dependency injection, tray menu |
| `src/Atoll.Core` | View models, contracts, models, design tokens |
| `src/Atoll.Services` | Media, lyrics, clipboard, notes, usage and settings services |
| `src/Atoll.Platform.Windows` | The notch window, tabs, live activities and Win32 / WinRT interop |
| `src/Atoll.UI` | Settings window |
| `tests/Atoll.UnitTests` | xUnit tests |
| `original/` | The original Swift source (git submodule), used as the reference |
| `docs/` | Architecture notes, parity matrix, known differences from macOS |

## Privacy

- **Lyrics** are looked up on [LRCLIB](https://lrclib.net) using only the song's title, artist and album.
- **LLM usage** is read from Claude Code's and Codex's local log files. To show rate limits it calls
  those services with the tool's own saved sign-in, read-only; it never refreshes or changes your credentials.
- Everything else (notes, clipboard history, settings) stays on your PC under `%LOCALAPPDATA%\Atoll`.

## Differences from the macOS app

Some things can't be done the same way on Windows, such as lock-screen widgets, hiding
Windows' own volume pop-up, and haptics. See
[docs/KNOWN_DIFFERENCES.md](docs/KNOWN_DIFFERENCES.md) for the full list.

## Credits

Atoll for Windows was made by **f4hz_**, inspired by
[Atoll](https://github.com/Ebullioscopic/Atoll) by the Atoll contributors, which itself
builds on [boring.notch](https://github.com/TheBoredTeam/boring.notch). Thanks to them
for the original design.

## License

The original Atoll is licensed under the
[GNU General Public License v3.0](https://github.com/Ebullioscopic/Atoll/blob/dev/LICENSE).
As a port of it, this project falls under the same license.
