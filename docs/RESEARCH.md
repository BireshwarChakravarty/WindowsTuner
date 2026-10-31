# Research: what really makes a neglected Windows PC fast again

This is the background research behind WindowsTuner's design: what slows a PC down after it has been off for
a long time, which fixes actually help, which popular "optimizations" are myths, and which Windows mechanisms
the app uses. Written October 2026.

---

## 1. Why a PC is slow after being switched off for months

When the PC comes back online, Windows has a backlog of work, and all of it starts at once:

| What's happening | Why it's slow | What to do |
|---|---|---|
| **Windows Update catch-up** | A PC that missed months of patches can't jump straight to the latest one. It installs prerequisite updates first, sometimes several cumulative updates and a feature update in a row. Each one downloads, unpacks (TiWorker / TrustedInstaller) and needs a restart. | Let it finish. Install everything, then **Restart**. Repeat until Windows Update says you're up to date. |
| **Post-update housekeeping** | After updates Windows re-indexes search, recompiles .NET assemblies (`mscorsvw`), optimizes components and refreshes app packages. This can hold CPU or disk at 100 % for a while. | Leave the PC on and plugged in for an hour. |
| **Microsoft Defender** | Definitions are months old. It updates them, then often runs a full scan (`MsMpEng.exe`). | Update definitions first (WindowsTuner does this as the second Tune-up step). |
| **Every app's own updater** | Chrome, Edge, Office, Steam, Adobe, Zoom, drivers and so on all wake up and update at the same time. | Update all apps in one controlled pass (winget) instead of 15 updaters fighting over the disk. |
| **OneDrive / cloud sync** | Re-checks every file. | Pause sync for a few hours if you need the PC right away. |
| **Fast Startup** | With Fast Startup on, *Shut down* hibernates the kernel. Drivers, leaks and half-installed updates survive "shutdowns" for weeks. | Use **Restart**, or turn off Fast Startup (Tweaks → Performance). |

Sources: [PCWorld](https://www.pcworld.com/article/539780/computer-running-slow-use-it-more-often.html),
[The Register on Windows Update catch-up](https://www.theregister.com/2026/04/14/windows_update_torture/),
[Microsoft Tech Community thread on this exact scenario](https://techcommunity.microsoft.com/discussions/windowsinsiderprogram/windows-pc-slow-after-leaving-off-for-months/4518378).

**Hardware is the ceiling.** If Windows lives on a mechanical hard disk (HDD), the single biggest speed-up
available is replacing it with an SSD — typically 5–10× faster boot, updates and app launches. No software
comes close. WindowsTuner detects the system drive's media type and says so on the dashboard. Likewise,
8 GB of RAM is the practical minimum for Windows 11 with a modern browser.

## 2. Windows support status (check this first)

* **Windows 10** reached end of support on **14 October 2025**. Free security fixes now only come through the
  consumer **Extended Security Updates (ESU)** program, which Microsoft extended in June 2026 to
  **12 October 2027**. ESU covers critical/important security fixes only — no features, no quality fixes.
  ([Help Net Security](https://www.helpnetsecurity.com/2026/06/26/microsoft-windows-10-free-security-updates-esu-program/),
  [BleepingComputer](https://bleepingcomputer.com/news/microsoft/microsoft-quietly-extends-free-windows-10-esu-support-to-october-2027),
  [TechRadar](https://www.techradar.com/computing/windows/microsoft-extends-windows-10-support-out-of-the-blue-consumers-now-get-updates-for-another-year-to-october-2027))
* **Windows 11** 23H2 and older are out of support on Home/Pro. Use 24H2 (build 26100) or newer.

The dashboard flags both cases.

## 3. Updating apps: winget

**winget** (Windows Package Manager) ships with Windows 10 1809+ and Windows 11 through the *App Installer*
Store package. It can update most desktop apps — including many it didn't install — as long as the app is
registered in *Programs and Features* and exists in the winget catalog.

* Unattended update of everything:
  `winget upgrade --all --include-unknown --silent --accept-package-agreements --accept-source-agreements`
  ([Microsoft Learn: upgrade command](https://learn.microsoft.com/windows/package-manager/winget/upgrade)).
* `--include-unknown` is needed for apps whose installed version winget can't determine.
* WindowsTuner updates apps **one at a time** (parallel installers conflict), respects a per-user **skip list**,
  and explains common failures (app still running, reboot needed, no network…).
* Parsing: winget's table output is localized and wide characters shift columns, so WindowsTuner parses rows
  right-to-left by whitespace tokens. If the optional `Microsoft.WinGet.Client` PowerShell module is installed it
  uses that instead for exact, structured data (winget can truncate long IDs in text output).

**Microsoft Store apps** are updated by asking the Store to scan via the MDM WMI bridge
(`MDM_EnterpriseModernAppManagement_AppManagement01.UpdateScanMethod`). The call returns immediately and the
Store updates in the background; it's the scripted equivalent of *Library → Get updates*
([Microsoft Learn](https://learn.microsoft.com/windows/win32/dmwmibridgeprov/mdm-enterprisemodernappmanagement-appmanagement01-updatescanmethod)).

## 4. Updating Windows and drivers: Windows Update Agent API

WindowsTuner talks to the same engine as the Settings app — the Windows Update Agent COM API
(`Microsoft.Update.Session` → `IUpdateSearcher.Search` → download → install)
([Microsoft Learn: WUA API](https://learn.microsoft.com/windows/win32/api/wuapi/)).

* Search criteria `IsInstalled=0 and IsHidden=0` returns software **and driver** updates; `Type='Software'` /
  `Type='Driver'` filter them. Windows Update returns at most one driver per device.
* Updates install one by one so a single failure doesn't block the rest; EULAs are accepted programmatically.
* "Receive updates for other Microsoft products" (Office, .NET, etc.) is the Microsoft Update service
  `7971f918-a847-4430-9279-4a52d1efe18d`, registered via `IUpdateServiceManager2.AddService2`.

### Drivers: what's safe

* **Windows Update** carries vendor-certified drivers — safe default.
* For the **newest GPU and chipset drivers**, use the vendor's official tool: NVIDIA App, AMD Software Adrenalin,
  Intel Driver & Support Assistant, and the PC maker's tool (Dell Command Update/SupportAssist, Lenovo Vantage,
  HP Support Assistant, MyASUS…). WindowsTuner detects the GPU/CPU/manufacturer and links the right ones.
* **Never use third-party "driver updater" programs.** Microsoft's own community guidance groups them with
  registry cleaners as not recommended; many bundle adware or install wrong drivers.
* Before big driver changes WindowsTuner can export all third-party drivers (`dism /online /export-driver`)
  so they can be restored with `pnputil /add-driver … /install`.
* Driver "age": Windows' in-box drivers all carry the placeholder date **21 June 2006**, so an old date only
  means something for third-party drivers. WindowsTuner ignores the placeholder.
* Device problems come from `Win32_PnPEntity.ConfigManagerErrorCode` (Device Manager error codes like
  Code 10 / 28 / 43).

## 5. Speeding up: what works

| Fix | Effect | How WindowsTuner does it |
|---|---|---|
| Install all updates, then restart | Fixes known performance bugs; stops the background update churn | Updates page, Tune-up |
| Fewer startup apps | Faster sign-in, more free RAM all day | Startup page — writes the same `StartupApproved` flags Task Manager uses (first byte `02` enabled / `03` disabled, followed by a FILETIME), so changes show in Task Manager too |
| Free disk space (keep ≥ 15–20 % free) | Updates, page file and temp files need room; a full disk is extremely slow | Cleanup page: temp files (only older than 24 h), Windows Update cache, Delivery Optimization cache, error reports, crash dumps, thumbnail and browser caches, Recycle Bin; WinSxS component cleanup via `DISM /StartComponentCleanup` |
| Remove preinstalled bloat | Less background activity, cleaner Start/search | Remove-bloat page (AppX), with system packages protected |
| Turn off Widgets, ads, suggestions, Bing in Start | Fewer background processes and network calls | Tweaks (★ recommended) |
| Fast Startup off | Real shutdowns; fewer "weird after weeks" problems | Tweak |
| Power plan | High/Ultimate performance on desktops | Performance page (`powercfg`) |
| SSD TRIM / HDD defrag | Keeps drives at full speed | `defrag C: /O` — Windows picks TRIM for SSDs and defrag for HDDs; it never defrags an SSD |
| Repair system files | Fixes corruption-induced crashes/slowness | `DISM /Online /Cleanup-Image /RestoreHealth` then `sfc /scannow` |
| Visual effects off / transparency off | Helps old CPUs and integrated GPUs | Tweaks → Performance |
| Game DVR off, Game Mode, HAGS | Gaming FPS / latency | Tweaks → Gaming |

## 6. Myths WindowsTuner deliberately does NOT implement

* **Registry cleaners.** Microsoft does not support them and isn't responsible for issues they cause; there's no
  measurable speed benefit and real risk of breaking apps
  ([TheWindowsClub summary of Microsoft's position](https://www.thewindowsclub.com/microsoft-support-registry-cleaners-windows),
  [Malwarebytes: "Registry Cleaners: Digital Snake Oil"](https://www.malwarebytes.com/?p=72289),
  [Microsoft Community answer](https://learn.microsoft.com/en-us/answers/a/4722642)).
* **"RAM boosters" / memory cleaners.** Free RAM is wasted RAM; Windows' memory manager already uses spare
  memory for cache and releases it instantly. Forcing it empty makes the next app launch *slower*.
* **Deleting Prefetch.** Prefetch speeds up app launches; deleting it slows the next boot.
* **Disabling the page file.** Causes crashes when memory runs out; gives no speed benefit.
* **Defragmenting SSDs.** Wears the SSD and does nothing; Windows' optimizer TRIMs SSDs instead.
* **Disabling dozens of services.** Gains are negligible on modern PCs and it breaks updates, printing, Bluetooth,
  Store apps, etc. WindowsTuner offers only a handful of well-understood service tweaks, marked Moderate/Advanced.
* **Third-party driver updaters.** See section 4.

## 7. Safety design

* **Restore point first.** Before batches of changes (Tune-up, many app updates, Windows updates, app removal,
  recommended tweaks, profiles) a System Restore point is created. Windows normally allows one per 24 h;
  WindowsTuner temporarily lifts that limit (`SystemRestorePointCreationFrequency`) and restores it afterwards.
* **Undo journal for tweaks.** The first time a tweak is turned on, the original registry values (or their
  absence) are saved to `%LOCALAPPDATA%\WindowsTuner\tweak-journal.json`; turning it off restores exactly those
  values. "Undo all my tweaks" reverts everything.
* **Protected app list.** The Store, App Installer (winget), Windows Security, frameworks and shell components can
  never be selected for removal.
* **Cleanup is conservative.** Never touches documents, passwords, history or cookies; temp files newer than
  24 hours are kept; locked files are skipped; junctions/symlinks are never followed.
* **Only official sources.** winget, Microsoft Store, Windows Update, built-in Windows tools.
