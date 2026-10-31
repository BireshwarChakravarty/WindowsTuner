using WindowsTuner.Core.Cleanup;
using WindowsTuner.Core.Health;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Performance;
using WindowsTuner.Core.Safety;
using WindowsTuner.Core.Updates;

namespace WindowsTuner.Core.Maintenance;

public enum StepOutcome { Pending, Running, Done, Warning, Failed, Skipped }

public sealed class TuneUpStep : ObservableObject
{
    private bool _isSelected;
    private StepOutcome _outcome;
    private string _detail = "";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string Duration { get; init; } = "";

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public StepOutcome Outcome { get => _outcome; set { if (Set(ref _outcome, value)) OnPropertyChanged(nameof(OutcomeText)); } }
    public string Detail { get => _detail; set => Set(ref _detail, value); }

    public string OutcomeText => Outcome switch
    {
        StepOutcome.Running => "Running…",
        StepOutcome.Done => "✔ Done",
        StepOutcome.Warning => "⚠ Done with warnings",
        StepOutcome.Failed => "✖ Failed",
        StepOutcome.Skipped => "Skipped",
        _ => "",
    };
}

public static class TuneUpSteps
{
    public const string RestorePoint = "restore-point";
    public const string Defender = "defender";
    public const string Cleanup = "cleanup";
    public const string Apps = "apps";
    public const string Store = "store";
    public const string Windows = "windows-update";
    public const string Drivers = "drivers";
    public const string Drives = "optimize-drives";
    public const string Components = "component-cleanup";
    public const string Repair = "repair";

    public static readonly string[] Defaults = [RestorePoint, Defender, Cleanup, Apps, Store, Windows, Drivers, Drives];

    public static IReadOnlyList<TuneUpStep> Create(ICollection<string> selected)
    {
        TuneUpStep[] steps =
    [
        new() { Id = RestorePoint, Name = "Create a restore point", Description = "Safety net so every change can be rolled back with System Restore.", Duration = "~1 min" },
        new() { Id = Defender, Name = "Update virus definitions", Description = "Microsoft Defender gets the latest definitions.", Duration = "~1 min" },
        new() { Id = Cleanup, Name = "Clean junk files", Description = "Temp files, update leftovers and error reports.", Duration = "1–3 min" },
        new() { Id = Apps, Name = "Update all apps (winget)", Description = "Chrome, Zoom, VLC, 7-Zip, Steam… everything winget knows about.", Duration = "2–20 min" },
        new() { Id = Store, Name = "Update Microsoft Store apps", Description = "Asks the Store to update its apps in the background.", Duration = "<1 min" },
        new() { Id = Windows, Name = "Install Windows updates", Description = "Security and quality updates from Windows Update.", Duration = "5–60 min" },
        new() { Id = Drivers, Name = "Install driver updates", Description = "Recommended drivers your hardware makers publish on Windows Update (optional ones are left for you to pick on the Updates page).", Duration = "2–15 min" },
        new() { Id = Drives, Name = "Optimize drives", Description = "TRIM for SSDs, defragment for hard disks.", Duration = "1–30 min" },
        new() { Id = Components, Name = "Clean up Windows components", Description = "Removes superseded update files from WinSxS (slow; old updates can't be uninstalled afterwards).", Duration = "5–30 min" },
        new() { Id = Repair, Name = "Repair Windows system files", Description = "DISM RestoreHealth + SFC — fixes corruption that causes crashes and slowness.", Duration = "15–45 min" },
    ];
        foreach (var s in steps) s.IsSelected = selected.Contains(s.Id);
        return steps;
    }
}

public sealed class TuneUpResult
{
    public bool RebootRequired { get; set; }
    public int Failed { get; set; }
    public long BytesFreed { get; set; }
    public int AppsUpdated { get; set; }
    public int WindowsUpdatesInstalled { get; set; }
}

/// <summary>The one-click maintenance pipeline, used by the UI and by the scheduled (headless) run.</summary>
public sealed class TuneUpRunner(AppSettings settings)
{
    private readonly WingetService _winget = new();
    private readonly WindowsUpdateService _wu = new();
    private readonly CleanupService _cleanup = new();

    public async Task<TuneUpResult> RunAsync(IReadOnlyList<TuneUpStep> steps, CancellationToken ct = default)
    {
        var result = new TuneUpResult();
        Log.Info("══════ Tune-up started ══════");
        foreach (var step in steps)
        {
            if (!step.IsSelected) { step.Outcome = StepOutcome.Skipped; continue; }
            ct.ThrowIfCancellationRequested();
            step.Outcome = StepOutcome.Running;
            Log.Info($"── {step.Name}");
            try
            {
                step.Outcome = await RunStepAsync(step, result, ct);
            }
            catch (OperationCanceledException)
            {
                step.Outcome = StepOutcome.Skipped;
                step.Detail = "Cancelled";
                throw;
            }
            catch (Exception ex)
            {
                step.Outcome = StepOutcome.Failed;
                step.Detail = ex.Message;
                result.Failed++;
                Log.Error($"{step.Name}: {ex.Message}");
            }
        }
        result.RebootRequired |= SystemInfo.SystemInfoService.IsRebootPending();
        Log.Info($"══════ Tune-up finished — {Format.Bytes(result.BytesFreed)} freed, {result.AppsUpdated} app(s) and {result.WindowsUpdatesInstalled} Windows update(s) installed" +
                 (result.RebootRequired ? ", RESTART REQUIRED" : "") + " ══════");
        return result;
    }

    private async Task<StepOutcome> RunStepAsync(TuneUpStep step, TuneUpResult result, CancellationToken ct)
    {
        switch (step.Id)
        {
            case TuneUpSteps.RestorePoint:
                var rp = await RestorePointService.CreateAsync("WindowsTuner tune-up", ct);
                step.Detail = rp ? "Created" : "Could not create (continuing)";
                return rp ? StepOutcome.Done : StepOutcome.Warning;

            case TuneUpSteps.Defender:
                var d = await HealthService.UpdateDefenderSignaturesAsync(ct);
                step.Detail = d.Success ? "Definitions up to date" : $"Exit code {d.ExitCode}";
                return d.Success ? StepOutcome.Done : StepOutcome.Warning;

            case TuneUpSteps.Cleanup:
                var targets = CleanupService.CreateDefaultTargets()
                    .Where(t => settings.CleanupTargets.Count > 0 ? settings.CleanupTargets.Contains(t.Id) : t.DefaultSelected)
                    .ToList();
                await _cleanup.AnalyzeAsync(targets, ct);
                var cr = await _cleanup.CleanAsync(targets, ct);
                result.BytesFreed += cr.BytesFreed;
                step.Detail = $"Freed {Format.Bytes(cr.BytesFreed)}";
                return StepOutcome.Done;

            case TuneUpSteps.Apps:
                if (!await _winget.IsAvailableAsync(ct))
                {
                    step.Detail = "winget not installed — get \"App Installer\" from the Microsoft Store";
                    return StepOutcome.Warning;
                }
                await _winget.UpdateSourcesAsync(ct);
                var apps = (await _winget.GetUpgradesAsync(settings.IncludeUnknownVersions, ct))
                    .Where(a => !settings.ExcludedAppIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase) && !a.RequiresExplicitTarget)
                    .ToList();
                if (apps.Count == 0) { step.Detail = "All apps are up to date"; return StepOutcome.Done; }
                var (ok, failed) = await _winget.UpgradeManyAsync(apps, ct: ct);
                result.AppsUpdated += ok;
                step.Detail = $"{ok} updated" + (failed > 0 ? $", {failed} failed (see log)" : "");
                return failed > 0 ? StepOutcome.Warning : StepOutcome.Done;

            case TuneUpSteps.Store:
                var st = await StoreUpdateService.TriggerStoreUpdatesAsync(ct);
                step.Detail = st ? "Store is updating apps in the background" : "Open the Store → Library → Get updates";
                return st ? StepOutcome.Done : StepOutcome.Warning;

            case TuneUpSteps.Windows:
            case TuneUpSteps.Drivers:
                var drivers = step.Id == TuneUpSteps.Drivers;
                var found = (await _wu.SearchAsync(includeDrivers: drivers, includeOptional: false, ct))
                    .Where(u => u.IsDriver == drivers && !u.IsOptional)
                    .ToList();
                if (found.Count == 0) { step.Detail = drivers ? "No driver updates" : "Windows is up to date"; return StepOutcome.Done; }
                var summary = await _wu.InstallAsync(found, ct: ct);
                result.WindowsUpdatesInstalled += summary.Installed;
                result.RebootRequired |= summary.RebootRequired;
                step.Detail = $"{summary.Installed} installed" + (summary.Failed > 0 ? $", {summary.Failed} failed" : "") + (summary.RebootRequired ? " — restart required" : "");
                return summary.Failed > 0 ? StepOutcome.Warning : StepOutcome.Done;

            case TuneUpSteps.Drives:
                var ok2 = await DriveOptimizer.OptimizeAllAsync(ct);
                step.Detail = ok2 ? "All drives optimized" : "Some drives could not be optimized";
                return ok2 ? StepOutcome.Done : StepOutcome.Warning;

            case TuneUpSteps.Components:
                var cc = await CleanupService.ComponentStoreCleanupAsync(ct);
                step.Detail = cc.Success ? "Component store cleaned" : $"DISM exit code {cc.ExitCode}";
                return cc.Success ? StepOutcome.Done : StepOutcome.Warning;

            case TuneUpSteps.Repair:
                var dism = await HealthService.RunDismRestoreHealthAsync(ct);
                var sfc = await HealthService.RunSfcAsync(ct);
                step.Detail = $"DISM {(dism.Success ? "OK" : $"exit {dism.ExitCode}")}, SFC {(sfc.Success ? "OK" : $"exit {sfc.ExitCode}")}";
                return dism.Success && sfc.Success ? StepOutcome.Done : StepOutcome.Warning;

            default:
                return StepOutcome.Skipped;
        }
    }
}
