using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Performance;

public static class DriveOptimizer
{
    /// <summary>
    /// Runs Windows' own optimizer with /O: TRIM (retrim) on SSDs, defragment on hard disks. It never defragments SSDs.
    /// </summary>
    public static async Task<bool> OptimizeAllAsync(CancellationToken ct = default)
    {
        var ok = true;
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var letter = d.Name.TrimEnd('\\');
            Log.Info($"Optimizing {letter} …");
            var r = await ProcessRunner.RunAsync("defrag.exe", $"{letter} /O /U /V", Log.Output, ct: ct);
            if (r.Success) Log.Success($"{letter} optimized.");
            else { ok = false; Log.Warn($"{letter}: defrag exited with {r.ExitCode}"); }
        }
        return ok;
    }

    public static void OpenOptimizeDrives() => Shell.Start("dfrgui.exe", "");
}
