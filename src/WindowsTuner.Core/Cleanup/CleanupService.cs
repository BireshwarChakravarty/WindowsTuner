using System.Diagnostics;
using System.Runtime.InteropServices;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Cleanup;

public sealed class CleanupService
{
    public static IReadOnlyList<CleanupTarget> CreateDefaultTargets() =>
    [
        new()
        {
            Id = "user-temp", Name = "Your temporary files", Group = "System", DefaultSelected = true,
            Description = "Leftovers from installers and apps in %TEMP%. Files touched in the last 24 hours are kept.",
            Paths = [@"%TEMP%"], MinAge = TimeSpan.FromHours(24),
        },
        new()
        {
            Id = "windows-temp", Name = "Windows temporary files", Group = "System", DefaultSelected = true, NeedsAdmin = true,
            Description = @"C:\Windows\Temp. Files touched in the last 24 hours are kept.",
            Paths = [@"%SystemRoot%\Temp"], MinAge = TimeSpan.FromHours(24),
        },
        new()
        {
            Id = "recycle-bin", Name = "Recycle Bin", Group = "System", DefaultSelected = false, Kind = CleanupKind.RecycleBin,
            Description = "Permanently deletes everything in the Recycle Bin on all drives.",
        },
        new()
        {
            Id = "wu-cache", Name = "Windows Update download cache", Group = "Windows Update", DefaultSelected = false, NeedsAdmin = true,
            Kind = CleanupKind.WindowsUpdateCache,
            Description = @"Already-installed update packages in C:\Windows\SoftwareDistribution\Download. Also fixes many 'update stuck' problems. Run AFTER updates have installed.",
            Paths = [@"%SystemRoot%\SoftwareDistribution\Download"],
        },
        new()
        {
            Id = "delivery-optimization", Name = "Delivery Optimization cache", Group = "Windows Update", DefaultSelected = true, NeedsAdmin = true,
            Kind = CleanupKind.DeliveryOptimization,
            Description = "Update files Windows keeps to share with other PCs.",
            Paths = [@"%SystemRoot%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"],
        },
        new()
        {
            Id = "error-reports", Name = "Windows error reports", Group = "Diagnostics", DefaultSelected = true, NeedsAdmin = true,
            Description = "Crash reports already queued or sent to Microsoft.",
            Paths = [@"%ProgramData%\Microsoft\Windows\WER\ReportArchive", @"%ProgramData%\Microsoft\Windows\WER\ReportQueue", @"%LOCALAPPDATA%\Microsoft\Windows\WER"],
        },
        new()
        {
            Id = "crash-dumps", Name = "Crash dumps", Group = "Diagnostics", DefaultSelected = false, NeedsAdmin = true,
            Description = "Memory dumps from app and system crashes. Keep them if you are troubleshooting blue screens.",
            Paths = [@"%LOCALAPPDATA%\CrashDumps", @"%SystemRoot%\Minidump", @"%SystemRoot%\LiveKernelReports"],
        },
        new()
        {
            Id = "thumbnails", Name = "Thumbnail cache", Group = "Explorer", DefaultSelected = false,
            Description = "Picture previews Explorer rebuilds automatically. Files in use are skipped.",
            Paths = [@"%LOCALAPPDATA%\Microsoft\Windows\Explorer"], FilePattern = "thumbcache_*.db",
        },
        new()
        {
            Id = "edge-cache", Name = "Microsoft Edge cache", Group = "Browsers", DefaultSelected = false, RequiresClosed = ["msedge"],
            Description = "Cached web files (not passwords, history or cookies). Edge must be closed.",
            Paths = [@"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Cache", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Code Cache", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\GPUCache"],
        },
        new()
        {
            Id = "chrome-cache", Name = "Google Chrome cache", Group = "Browsers", DefaultSelected = false, RequiresClosed = ["chrome"],
            Description = "Cached web files (not passwords, history or cookies). Chrome must be closed.",
            Paths = [@"%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache", @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Code Cache", @"%LOCALAPPDATA%\Google\Chrome\User Data\*\GPUCache"],
        },
        new()
        {
            Id = "firefox-cache", Name = "Mozilla Firefox cache", Group = "Browsers", DefaultSelected = false, RequiresClosed = ["firefox"],
            Description = "Cached web files (not passwords, history or cookies). Firefox must be closed.",
            Paths = [@"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2"],
        },
        new()
        {
            Id = "directx-shader", Name = "DirectX shader cache", Group = "Graphics", DefaultSelected = false,
            Description = "Compiled game shaders. Rebuilt automatically (first launch of games may stutter briefly).",
            Paths = [@"%LOCALAPPDATA%\D3DSCache"],
        },
    ];

    public Task AnalyzeAsync(IEnumerable<CleanupTarget> targets, CancellationToken ct = default) => Task.Run(() =>
    {
        foreach (var t in targets)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (t.Kind == CleanupKind.RecycleBin)
                {
                    var (size, items) = QueryRecycleBin();
                    t.SizeBytes = size;
                    t.FileCount = (int)items;
                }
                else
                {
                    var dirs = t.Paths.SelectMany(FileScanner.ExpandPattern).ToList();
                    var (bytes, files) = FileScanner.Measure(dirs, t.FilePattern, t.MinAge, ct);
                    t.SizeBytes = bytes;
                    t.FileCount = files;
                }
                t.Status = "";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                t.SizeBytes = 0;
                t.Status = ex.Message;
            }
        }
    }, ct);

    public async Task<CleanResult> CleanAsync(IEnumerable<CleanupTarget> targets, CancellationToken ct = default)
    {
        long freed = 0;
        int deleted = 0, skipped = 0;
        foreach (var t in targets)
        {
            ct.ThrowIfCancellationRequested();
            var running = t.RequiresClosed.Where(p => Process.GetProcessesByName(p).Length > 0).ToList();
            if (running.Count > 0)
            {
                t.Status = $"Skipped — close {string.Join(", ", running)} first";
                Log.Warn($"{t.Name}: {t.Status}");
                continue;
            }

            Log.Info($"Cleaning {t.Name}…");
            var before = t.SizeBytes;
            CleanResult r;
            switch (t.Kind)
            {
                case CleanupKind.RecycleBin:
                    var (size, _) = QueryRecycleBin();
                    EmptyRecycleBin();
                    r = new CleanResult(size, 0, 0);
                    break;
                case CleanupKind.WindowsUpdateCache:
                    r = await CleanWindowsUpdateCacheAsync(t, ct);
                    break;
                case CleanupKind.DeliveryOptimization:
                    await PowerShell.RunAsync("Delete-DeliveryOptimizationCache -Force -ErrorAction SilentlyContinue", Log.Output, ct);
                    r = await Task.Run(() => FileScanner.Delete(t.Paths.SelectMany(FileScanner.ExpandPattern), t.FilePattern, t.MinAge, ct), ct);
                    if (before > 0) r = r with { BytesFreed = Math.Max(r.BytesFreed, before) };
                    break;
                default:
                    r = await Task.Run(() => FileScanner.Delete(t.Paths.SelectMany(FileScanner.ExpandPattern), t.FilePattern, t.MinAge, ct), ct);
                    break;
            }

            // "Hyper admin": files even an elevated admin can't delete are retried as SYSTEM (fixed, whitelisted folders only).
            if (r.FilesSkipped > 0 && t.NeedsAdmin && t.Kind == CleanupKind.Files && SystemRunner.Enabled)
            {
                var dirs = t.Paths.SelectMany(FileScanner.ExpandPattern).Where(SystemRunner.IsSafeSystemCleanupFolder).ToList();
                if (dirs.Count > 0)
                {
                    var (beforeBytes, _) = FileScanner.Measure(dirs, t.FilePattern, t.MinAge, ct);
                    foreach (var dir in dirs)
                        await SystemRunner.CleanFolderAsync(dir, (int)Math.Ceiling(t.MinAge.TotalDays), ct);
                    var (afterBytes, afterFiles) = FileScanner.Measure(dirs, t.FilePattern, t.MinAge, ct);
                    r = new CleanResult(r.BytesFreed + Math.Max(0, beforeBytes - afterBytes), r.FilesDeleted, afterFiles);
                }
            }

            freed += r.BytesFreed;
            deleted += r.FilesDeleted;
            skipped += r.FilesSkipped;
            t.Status = $"Freed {Format.Bytes(r.BytesFreed)}" + (r.FilesSkipped > 0 ? $" ({r.FilesSkipped} in use, skipped)" : "");
            t.SizeBytes = Math.Max(0, (before < 0 ? 0 : before) - r.BytesFreed);
            Log.Success($"{t.Name}: {t.Status}");
        }
        return new CleanResult(freed, deleted, skipped);
    }

    private static async Task<CleanResult> CleanWindowsUpdateCacheAsync(CleanupTarget t, CancellationToken ct)
    {
        // The Windows Update and BITS services hold these files open.
        await ProcessRunner.RunAsync("net.exe", "stop wuauserv /y", Log.Output, ct: ct);
        await ProcessRunner.RunAsync("net.exe", "stop bits /y", Log.Output, ct: ct);
        try
        {
            return await Task.Run(() => FileScanner.Delete(t.Paths.SelectMany(FileScanner.ExpandPattern), t.FilePattern, t.MinAge, ct), ct);
        }
        finally
        {
            await ProcessRunner.RunAsync("net.exe", "start bits", Log.Output, ct: CancellationToken.None);
            await ProcessRunner.RunAsync("net.exe", "start wuauserv", Log.Output, ct: CancellationToken.None);
        }
    }

    /// <summary>
    /// Removes superseded versions of Windows components (WinSxS). Safe, but slow, and installed updates can no
    /// longer be uninstalled afterwards.
    /// </summary>
    public static Task<ProcessResult> ComponentStoreCleanupAsync(CancellationToken ct = default) =>
        ProcessRunner.RunAsync("dism.exe", "/Online /Cleanup-Image /StartComponentCleanup", Log.Output, ct: ct);

    public static Task<ProcessResult> AnalyzeComponentStoreAsync(CancellationToken ct = default) =>
        ProcessRunner.RunAsync("dism.exe", "/Online /Cleanup-Image /AnalyzeComponentStore", Log.Output, ct: ct);

    public static void OpenStorageSense() => Shell.Open("ms-settings:storagesense");
    public static void OpenDiskCleanup() => Shell.Start("cleanmgr.exe", "/d C:");

    #region Recycle Bin (shell32)

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SherbNoConfirmation = 0x1, SherbNoProgressUi = 0x2, SherbNoSound = 0x4;

    public static (long Size, long Items) QueryRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? (info.i64Size, info.i64NumItems) : (0, 0);
    }

    public static void EmptyRecycleBin() =>
        SHEmptyRecycleBinW(IntPtr.Zero, null, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);

    #endregion
}
