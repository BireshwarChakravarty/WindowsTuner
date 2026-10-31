using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Updates;

/// <summary>Software that updates through its own channel rather than winget / Windows Update.</summary>
public sealed class ExtraUpdateSource : ObservableObject
{
    private bool _isInstalled;
    private bool _isSelected;
    private string _status = "";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool DefaultSelected { get; init; } = true;
    public required Func<bool> Detect { get; init; }
    public required Func<CancellationToken, Task<ProcessResult>> Update { get; init; }

    public bool IsInstalled { get => _isInstalled; set { if (Set(ref _isInstalled, value)) OnPropertyChanged(nameof(InstalledText)); } }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string InstalledText => IsInstalled ? "Installed" : "Not installed";
}

public static class ExtraUpdateService
{
    private static string ProgramFiles => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string ProgramData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

    private static string OfficeC2R => Path.Combine(ProgramFiles, "Common Files", "microsoft shared", "ClickToRun", "OfficeC2RClient.exe");

    private static string? FindOnPath(string exe)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim(), exe);
                if (File.Exists(p)) return p;
            }
            catch { }
        }
        return null;
    }

    private static string Choco => Path.Combine(Environment.GetEnvironmentVariable("ChocolateyInstall") ?? Path.Combine(ProgramData, "chocolatey"), "bin", "choco.exe");

    public static IReadOnlyList<ExtraUpdateSource> CreateSources() =>
    [
        new()
        {
            Id = "office", Name = "Microsoft Office / Microsoft 365",
            Description = "Word, Excel, Outlook and the rest update through Office's own Click-to-Run service. Open Office apps may be asked to close.",
            Detect = () => File.Exists(OfficeC2R),
            Update = ct => ProcessRunner.RunAsync(OfficeC2R, "/update user updatepromptuser=false forceappshutdown=false displaylevel=false", Log.Output, ct: ct),
        },
        new()
        {
            Id = "defender-platform", Name = "Microsoft Defender engine & definitions",
            Description = "Antivirus definitions, engine and platform updates.",
            Detect = () => true,
            Update = ct => Health.HealthService.UpdateDefenderSignaturesAsync(ct),
        },
        new()
        {
            Id = "wsl", Name = "Windows Subsystem for Linux",
            Description = "Updates the WSL kernel and runtime (your Linux distros update with their own package manager, e.g. apt).",
            Detect = () => File.Exists(Path.Combine(ProgramFiles, "WSL", "wsl.exe")),
            Update = ct => ProcessRunner.RunAsync(Path.Combine(System32, "wsl.exe"), "--update", Log.Output, System.Text.Encoding.Unicode, ct),
        },
        new()
        {
            Id = "choco", Name = "Chocolatey packages",
            Description = "Upgrades every package installed with Chocolatey (choco upgrade all).",
            Detect = () => File.Exists(Choco),
            Update = ct => ProcessRunner.RunAsync(Choco, "upgrade all -y --no-progress", Log.Output, ct: ct),
        },
        new()
        {
            Id = "scoop", Name = "Scoop packages",
            Description = "Updates Scoop itself and every app installed with it.",
            Detect = () => Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("SCOOP") ?? Path.Combine(UserProfile, "scoop"), "shims")),
            Update = ct => PowerShell.RunAsync("scoop update; scoop update *", Log.Output, ct),
        },
        new()
        {
            Id = "psmodules", Name = "PowerShell modules",
            Description = "Updates modules installed from the PowerShell Gallery (Update-Module).",
            Detect = () => Directory.Exists(Path.Combine(ProgramFiles, "WindowsPowerShell", "Modules")),
            Update = ct => PowerShell.RunAsync(
                "Get-InstalledModule -ErrorAction SilentlyContinue | ForEach-Object { try { Update-Module -Name $_.Name -Force -ErrorAction Stop; Write-Output \"Updated $($_.Name)\" } catch { Write-Output \"Skipped $($_.Name): $($_.Exception.Message)\" } }",
                Log.Output, ct),
        },
        new()
        {
            Id = "npm", Name = "Node.js global packages (npm)", DefaultSelected = false,
            Description = "Updates globally installed npm packages (npm update -g). Off by default — developers may pin versions.",
            Detect = () => FindOnPath("npm.cmd") is not null,
            Update = ct => ProcessRunner.RunAsync("cmd.exe", "/d /c npm update -g", Log.Output, ct: ct),
        },
        new()
        {
            Id = "store", Name = "Microsoft Store apps",
            Description = "Asks the Store to update all Store apps in the background.",
            Detect = () => true,
            Update = async ct => await StoreUpdateService.TriggerStoreUpdatesAsync(ct) ? new ProcessResult(0, "", "") : new ProcessResult(1, "", ""),
        },
    ];

    public static void DetectAll(IEnumerable<ExtraUpdateSource> sources)
    {
        foreach (var s in sources)
        {
            try { s.IsInstalled = s.Detect(); }
            catch { s.IsInstalled = false; }
            s.IsSelected = s.IsInstalled && s.DefaultSelected;
            if (!s.IsInstalled) s.Status = "";
        }
    }

    public static async Task<(int Ok, int Failed)> UpdateAsync(IEnumerable<ExtraUpdateSource> sources, CancellationToken ct = default)
    {
        int ok = 0, failed = 0;
        foreach (var s in sources.Where(s => s.IsInstalled && s.IsSelected))
        {
            ct.ThrowIfCancellationRequested();
            s.Status = "Updating…";
            Log.Info($"Updating {s.Name}…");
            try
            {
                var r = await s.Update(ct);
                if (r.Success) { ok++; s.Status = "✔ Up to date"; Log.Success($"{s.Name} updated."); }
                else { failed++; s.Status = $"Finished with code {r.ExitCode} (see log)"; Log.Warn($"{s.Name}: exit code {r.ExitCode}"); }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                s.Status = "Failed: " + ex.Message;
                Log.Error($"{s.Name}: {ex.Message}");
            }
        }
        return (ok, failed);
    }
}
