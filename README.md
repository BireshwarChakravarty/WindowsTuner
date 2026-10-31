# WindowsTuner

**Update everything, speed it up, make it yours** — a free, open-source Windows 10/11 app that brings a slow,
out-of-date PC back into shape using only Windows' own tools and official update sources.

![icon](src/WindowsTuner.App/Assets/WindowsTuner.png)

## What it does

| Page | What you get |
|---|---|
| **Dashboard** | Health score, CPU / RAM / disk / uptime, and a prioritized "what to do next" list with one-click fixes (pending restart, Windows 10 end-of-support, full disk, HDD, too many startup apps, out-of-date apps, stale virus definitions, failing disks…). |
| **One-click Tune-up** | Restore point → Defender definitions → junk cleanup → **all app updates** → Store app updates → **Windows updates** → **driver updates** → drive optimization (optional: component cleanup, DISM+SFC repair). Can run **weekly in the background**. |
| **Updates** | App updates through **winget** (Chrome, Zoom, 7-Zip, Steam, VLC… thousands of apps) with select / update all / skip-forever; Microsoft Store updates; **Windows Update and driver updates** through the Windows Update Agent API; optional "other Microsoft products". **Everything else** tab: Microsoft Office / 365 (Click-to-Run), Defender engine, WSL, Chocolatey, Scoop, PowerShell modules, npm globals. |
| **Security** | One-page checkup: firewall, real-time protection, definitions age, tamper protection, UAC, SMBv1, paused Windows Update, Secure Boot, TPM, BitLocker, Remote Desktop, auto sign-in, Guest account, SmartScreen, memory integrity — with one-click fixes. |
| **Drivers** | Problem devices with plain-English error codes, all third-party drivers with age, links to the *official* vendor tools for your exact hardware (NVIDIA / AMD / Intel / Dell / Lenovo / HP / ASUS…), driver backup, hardware rescan. |
| **Clean up** | Temp files, Windows Update cache, Delivery Optimization, error reports, crash dumps, thumbnail / browser / shader caches, Recycle Bin; WinSxS component cleanup. Shows sizes before deleting. |
| **Startup apps** | Turn startup apps on/off (same mechanism as Task Manager), with publisher and broken-entry detection. |
| **Background tasks** | The hidden startup items Task Manager doesn't show: third-party **scheduled tasks** (toggle on/off) and **services** (Automatic / Manual / Disabled, "set all updaters to Manual", restore originals). |
| **Programs & files** | Every installed desktop program with size and install date, sort/search, run its uninstaller; **large-file finder** across your user folders and **old installers** in Downloads, sent to the Recycle Bin. |
| **Network** | DNS speed test (your DNS vs Cloudflare, Google, Quad9, OpenDNS, Cloudflare Family) and one-click switch/reset; flush DNS; reset Winsock/TCP-IP; adapter overview. |
| **Remove bloat** | Remove preinstalled Store apps (News, Solitaire, Clipchamp, Candy Crush…) with ratings; system apps are protected. |
| **Performance** | Live "what's using my PC" (grouped by app), end tasks, power plans incl. Ultimate Performance, TRIM/defrag. |
| **Tweaks** | 50+ reversible tweaks across Explorer, Taskbar & Start, Privacy & Ads, Performance, Gaming, Appearance, Windows Update, Services and Input. Search, categories, risk levels, ★ recommended preset, **save/load profiles**, **add your own tweaks** in JSON, **undo everything**. |
| **Health & repair** | What the **event logs** say about the last 30 days (blue screens, unexpected shutdowns, disk / file-system / WHEA hardware errors, top crashing apps, boot time), **battery wear**, BIOS age, disk health (SMART wear/temperature), DISM + SFC repair, Defender update & quick scan, chkdsk online scan, memory test, restore points, Reliability Monitor. |
| **Settings** | Dark / light / follow-Windows theme, any accent colour, skip list, restore-point behaviour. |

### "Hyper-admin" mode

WindowsTuner always runs elevated (administrator). With **hyper-admin mode** (Settings, off by default) it can
also run specific, fixed operations as the built-in **SYSTEM** account through a one-shot Task Scheduler job —
for the few things an administrator is denied: protected Windows temp/report/dump folders, and vendor services
or scheduled tasks that lock administrators out. Cleanup as SYSTEM is restricted to a whitelist of folder names
inside `C:\Windows` / `C:\ProgramData`. It deliberately does **not** run as TrustedInstaller or disable Windows'
own protections (Tamper Protection, protected system files) — doing so gives no speed benefit and is how
"optimizers" brick Windows.

Everything that changes your system is logged (`%LOCALAPPDATA%\WindowsTuner\Logs`) and either reversible or
preceded by a System Restore point.

**What it deliberately doesn't do:** registry cleaning, "RAM boosting", Prefetch deletion, SSD defragging,
mass service disabling, third-party driver downloads. These don't make PCs faster and can break them —
see [docs/RESEARCH.md](docs/RESEARCH.md) for the reasoning and sources.

## Get it

1. Download **WindowsTuner-win-x64** from the latest **[Actions build](../../actions/workflows/build.yml)**
   (or `WindowsTuner-x64.zip` from **Releases** once a version is tagged). Use the ARM64 build only on
   Snapdragon PCs.
2. **Unzip the whole folder** (right-click → Extract All) — don't run it from inside the zip.
3. Run `WindowsTuner.exe` from that folder and allow administrator access.
4. The app isn't code-signed yet, so SmartScreen may say *"Windows protected your PC"* → **More info → Run anyway**.

The folder contains `WindowsTuner.exe`, `WindowsTuner.dll`, `WindowsTuner.Core.dll` and Microsoft's own
(Microsoft-signed) .NET runtime files, so no .NET install is needed.

### If Microsoft Defender or another antivirus blocks it

New, unsigned tools that do system maintenance are a common **false positive**: they legitimately do things
malware also does (run PowerShell, change services, use Task Scheduler, edit the registry). WindowsTuner is
built so that this is as unlikely as possible:

* PowerShell runs as plain, readable `.ps1` files with `-File` — never `-EncodedCommand`, never
  `-ExecutionPolicy Bypass`.
* Helper scripts live in `C:\ProgramData\WindowsTuner\Scripts`, locked to Administrators/SYSTEM, and are deleted
  right after use — never in a world-writable temp folder.
* SYSTEM-level ("hyper-admin") operations are **off by default**.
* It ships as a normal folder rather than a compressed self-extracting `.exe`, has full version information, and
  every CI build is scanned with Microsoft Defender.

If it's still flagged:

1. Check that you downloaded it from this repository's Actions/Releases page, and compare the hash with
   `SHA256SUMS.txt`: `Get-FileHash .\WindowsTuner.exe`.
2. Note the **threat name** shown in Windows Security → Protection history. Names ending in `!ml`
   (e.g. `Trojan:Win32/Wacatac.B!ml`) are machine-learning guesses, the typical false positive.
3. Report it to Microsoft as a false positive at
   <https://www.microsoft.com/en-us/wdsi/filesubmission> (choose *Incorrectly detected as malware*). Microsoft
   usually clears it within a day or two, for everyone.
4. Only then, if you trust the build, use **Protection history → Actions → Allow on device**. Don't switch
   Defender off.

The real long-term fix is code signing (for example Azure Trusted Signing, or a free open-source certificate
from SignPath.io); signed builds build up reputation with SmartScreen and Defender.

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
dotnet publish src/WindowsTuner.App -c Release -r win-x64 --self-contained -o publish/win-x64
```

The engine (`WindowsTuner.Core`) targets plain `net10.0`, so its parsers, tweak engine, cleanup scanner and
recommendation rules are unit-tested on any OS; the WPF app builds on Linux/macOS with `EnableWindowsTargeting`.
CI runs the tests on Windows, publishes x64/ARM64 builds, scans them with Microsoft Defender, and launches the
real app with `--smoke` (opens every page and saves screenshots).

Command-line switches:

| Switch | Purpose |
|---|---|
| `--tune-up` | Headless run of the saved Tune-up steps (used by the weekly scheduled task). Exit code 0 = OK, 2 = some steps failed. |
| `--smoke` | Opens every page, writes `Logs\smoke.log` and screenshots, exits 0/1. |

## Project layout

```
src/WindowsTuner.Core      Engine: winget, Windows Update (COM), drivers (WMI), cleanup, startup,
                           tweak engine + catalog, power plans, health, AppX, recommendations, tune-up, scheduler,
                           extra updaters, installed programs, large files, scheduled tasks & services,
                           DNS benchmark & network repair, security checkup, event logs, battery, SYSTEM runner
src/WindowsTuner.App       WPF UI (dark/light themes, 15 pages)
tests/WindowsTuner.Tests   xUnit tests (run anywhere)
docs/RESEARCH.md           The research behind the design, with sources
```

## License

MIT
