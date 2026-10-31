# WindowsTuner

**Update everything, speed it up, make it yours** — a free, open-source Windows 10/11 app that brings a slow,
out-of-date PC back into shape using only Windows' own tools and official update sources.

![icon](src/WindowsTuner.App/Assets/WindowsTuner.png)

## What it does

| Page | What you get |
|---|---|
| **Dashboard** | Health score, CPU / RAM / disk / uptime, and a prioritized "what to do next" list with one-click fixes (pending restart, Windows 10 end-of-support, full disk, HDD, too many startup apps, out-of-date apps, stale virus definitions, failing disks…). |
| **One-click Tune-up** | Restore point → Defender definitions → junk cleanup → **all app updates** → Store app updates → **Windows updates** → **driver updates** → drive optimization (optional: component cleanup, DISM+SFC repair). Can run **weekly in the background**. |
| **Updates** | App updates through **winget** (Chrome, Zoom, 7-Zip, Steam, VLC… thousands of apps) with select / update all / skip-forever; Microsoft Store updates; **Windows Update and driver updates** through the Windows Update Agent API; optional "other Microsoft products" (Office). |
| **Drivers** | Problem devices with plain-English error codes, all third-party drivers with age, links to the *official* vendor tools for your exact hardware (NVIDIA / AMD / Intel / Dell / Lenovo / HP / ASUS…), driver backup, hardware rescan. |
| **Clean up** | Temp files, Windows Update cache, Delivery Optimization, error reports, crash dumps, thumbnail / browser / shader caches, Recycle Bin; WinSxS component cleanup. Shows sizes before deleting. |
| **Startup apps** | Turn startup apps on/off (same mechanism as Task Manager), with publisher and broken-entry detection. |
| **Remove bloat** | Remove preinstalled Store apps (News, Solitaire, Clipchamp, Candy Crush…) with ratings; system apps are protected. |
| **Performance** | Live "what's using my PC" (grouped by app), end tasks, power plans incl. Ultimate Performance, TRIM/defrag. |
| **Tweaks** | 50+ reversible tweaks across Explorer, Taskbar & Start, Privacy & Ads, Performance, Gaming, Appearance, Windows Update, Services and Input. Search, categories, risk levels, ★ recommended preset, **save/load profiles**, **add your own tweaks** in JSON, **undo everything**. |
| **Health & repair** | Disk health (SMART wear/temperature), DISM + SFC repair, Defender update & quick scan, chkdsk online scan, memory test, restore points, Reliability Monitor. |
| **Settings** | Dark / light / follow-Windows theme, any accent colour, skip list, restore-point behaviour. |

Everything that changes your system is logged (`%LOCALAPPDATA%\WindowsTuner\Logs`) and either reversible or
preceded by a System Restore point.

**What it deliberately doesn't do:** registry cleaning, "RAM boosting", Prefetch deletion, SSD defragging,
mass service disabling, third-party driver downloads. These don't make PCs faster and can break them —
see [docs/RESEARCH.md](docs/RESEARCH.md) for the reasoning and sources.

## Get it

1. Download `WindowsTuner.exe` from the latest **[Actions build](../../actions/workflows/build.yml)** (artifact
   *WindowsTuner-win-x64*) or from **Releases** once a version is tagged.
2. Run it. Windows asks for administrator rights (needed for updates and repairs).
   Because the exe isn't code-signed, SmartScreen may say *"Windows protected your PC"* → **More info → Run anyway**.
3. Start with the **Dashboard**, then **One-click Tune-up**.

It's a single self-contained file (no .NET install needed). x64 and ARM64 builds are produced.

### Recommended order for a PC that's been off for months

1. Plug in, connect to the internet, open WindowsTuner → **Updates → Windows & drivers → Check → Install**. Restart.
   Repeat until nothing is left (old PCs need several rounds).
2. **Updates → Apps → Update all**, and **Update Microsoft Store apps**.
3. **Startup apps**: switch off everything you don't need at sign-in.
4. **Clean up** → Analyze → Clean. **Remove bloat** → Select suggested → Remove.
5. **Tweaks → Apply recommended.** Then customize to taste.
6. **Health & repair → Repair (DISM + SFC)** if anything still crashes or misbehaves.
7. Turn on the **weekly Tune-up** so it never gets this bad again.

## Customize with your own tweaks

Tweaks → **Custom tweaks…** opens `%LOCALAPPDATA%\WindowsTuner\custom-tweaks.json`. Use the same format as
the [built-in catalog](src/WindowsTuner.Core/Tweaks/tweaks.json):

```jsonc
{
  "tweaks": [
    {
      "id": "custom.disableAeroShake",
      "category": "Custom",
      "name": "Disable Aero Shake",
      "description": "Shaking a window no longer minimizes the others.",
      "risk": "Safe",            // Safe | Moderate | Advanced
      "restart": "Explorer",     // None | Explorer | SignOut | Reboot
      "minBuild": 22000,         // optional: Windows 11 only
      "registry": [
        { "hive": "HKCU", "path": "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
          "name": "DisallowShaking", "kind": "DWord", "on": 1, "off": null }   // null = delete value
      ],
      "onCommands": [], "offCommands": []   // optional shell commands
    }
  ]
}
```

Click **Reload**. Custom tweaks get the same undo journal, profiles and search as built-in ones. A custom tweak
with the same `id` as a built-in one replaces it.

## Build from source

Requires the .NET 10 SDK.

```bash
dotnet test tests/WindowsTuner.Tests
dotnet publish src/WindowsTuner.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The engine (`WindowsTuner.Core`) targets plain `net10.0`, so its parsers, tweak engine, cleanup scanner and
recommendation rules are unit-tested on any OS; the WPF app builds on Linux/macOS with `EnableWindowsTargeting`.
CI runs the tests on Windows, publishes x64/ARM64 single-file builds, and launches the real app with `--smoke`
(opens every page and saves screenshots).

Command-line switches:

| Switch | Purpose |
|---|---|
| `--tune-up` | Headless run of the saved Tune-up steps (used by the weekly scheduled task). Exit code 0 = OK, 2 = some steps failed. |
| `--smoke` | Opens every page, writes `Logs\smoke.log` and screenshots, exits 0/1. |

## Project layout

```
src/WindowsTuner.Core      Engine: winget, Windows Update (COM), drivers (WMI), cleanup, startup,
                           tweak engine + catalog, power plans, health, AppX, recommendations, tune-up, scheduler
src/WindowsTuner.App       WPF UI (dark/light themes, 11 pages)
tests/WindowsTuner.Tests   xUnit tests (run anywhere)
docs/RESEARCH.md           The research behind the design, with sources
```

## License

MIT
