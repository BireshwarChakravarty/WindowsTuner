using System.Text;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Updates;

/// <summary>
/// Application updates through the Windows Package Manager (winget). winget ships with Windows 10 1809+ and
/// Windows 11 via the "App Installer" Store package and can update most desktop apps, even ones it did not install,
/// as long as they are registered in Programs and Features and exist in the winget catalog.
/// </summary>
public sealed class WingetService
{
    private const string Agreements = "--accept-source-agreements --disable-interactivity";

    private string? _exe;

    public string Executable => _exe ??= ResolveExecutable();

    private static string ResolveExecutable()
    {
        // The App Execution Alias lives in %LOCALAPPDATA%\Microsoft\WindowsApps.
        var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(alias) ? alias : "winget.exe";
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync(Executable, "--version", ct: ct);
        return r.Success;
    }

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var r = await ProcessRunner.RunAsync(Executable, "--version", ct: ct);
        return r.Success ? r.StdOut.Trim() : "not installed";
    }

    public Task<ProcessResult> UpdateSourcesAsync(CancellationToken ct = default) =>
        ProcessRunner.RunAsync(Executable, "source update", Log.Output, ct: ct);

    /// <summary>Lists available upgrades. Uses the structured PowerShell module when present, otherwise parses the CLI table.</summary>
    public async Task<IReadOnlyList<AppUpdate>> GetUpgradesAsync(bool includeUnknown, CancellationToken ct = default)
    {
        var viaModule = await TryGetUpgradesViaModuleAsync(ct);
        if (viaModule is not null)
        {
            Log.Info($"winget (PowerShell module) found {viaModule.Count} app update(s).");
            return viaModule;
        }

        var args = $"upgrade {Agreements}" + (includeUnknown ? " --include-unknown" : "");
        var result = await ProcessRunner.RunAsync(Executable, args, ct: ct);
        if (result.ExitCode != 0 && result.StdOut.Length == 0)
        {
            throw new InvalidOperationException(
                "winget is not available. Install or update \"App Installer\" from the Microsoft Store " +
                "(https://apps.microsoft.com/detail/9NBLGGH4NNS1). " + result.StdErr.Trim());
        }

        var updates = WingetParser.ParseUpgradeTable(result.StdOut);
        Log.Info($"winget found {updates.Count} app update(s).");
        return updates;
    }

    private static async Task<IReadOnlyList<AppUpdate>?> TryGetUpgradesViaModuleAsync(CancellationToken ct)
    {
        const string script = """
            $ErrorActionPreference = 'Stop'
            if (-not (Get-Module -ListAvailable -Name Microsoft.WinGet.Client)) { Write-Output '__NOMODULE__'; exit 0 }
            Import-Module Microsoft.WinGet.Client
            $items = @(Get-WinGetPackage | Where-Object { $_.IsUpdateAvailable } | ForEach-Object {
                [pscustomobject]@{
                    Name = $_.Name; Id = $_.Id; Version = $_.InstalledVersion
                    Available = @($_.AvailableVersions)[0]; Source = $_.Source
                }
            })
            ConvertTo-Json -InputObject $items -Compress -Depth 3
            """;
        try
        {
            var r = await PowerShell.RunAsync(script, ct: ct);
            if (!r.Success || r.StdOut.Contains("__NOMODULE__")) return null;
            var json = r.StdOut.Trim();
            var start = json.IndexOfAny(['[', '{']);
            return start < 0 ? [] : WingetParser.ParseModuleJson(json[start..]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Installs the Microsoft.WinGet.Client module for more accurate (non-truncated) results.</summary>
    public Task<ProcessResult> InstallPowerShellModuleAsync(CancellationToken ct = default) =>
        PowerShell.RunAsync("""
            $ErrorActionPreference = 'Stop'
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser | Out-Null
            Install-Module -Name Microsoft.WinGet.Client -Force -Scope CurrentUser -AllowClobber -Repository PSGallery
            Write-Output 'Microsoft.WinGet.Client installed.'
            """, Log.Output, ct);

    public async Task<bool> UpgradeAsync(AppUpdate app, CancellationToken ct = default)
    {
        if (app.IsIdTruncated)
        {
            app.Status = UpdateStatus.Skipped;
            app.Message = "ID was truncated by winget; install the WinGet PowerShell module (Settings) to update this one.";
            Log.Warn($"{app.Name}: {app.Message}");
            return false;
        }

        app.Status = UpdateStatus.Updating;
        app.Message = "Downloading and installing…";
        Log.Info($"Updating {app.Name} ({app.Id}) {app.InstalledVersion} → {app.AvailableVersion}");

        var args = new StringBuilder($"upgrade --id \"{app.Id}\" --exact --silent --accept-package-agreements {Agreements}");
        if (!string.IsNullOrEmpty(app.Source)) args.Append($" --source \"{app.Source}\"");
        if (app.IsVersionUnknown) args.Append(" --include-unknown");
        if (app.RequiresExplicitTarget) args.Append(" --include-pinned");

        var result = await ProcessRunner.RunAsync(Executable, args.ToString(), Log.Output, ct: ct);
        var code = unchecked((uint)result.ExitCode);
        if (result.Success || code == 0x8A150109)
        {
            app.Status = UpdateStatus.Updated;
            app.Message = code == 0x8A150109 ? "Updated — restart required" : "Updated";
            Log.Success($"{app.Name} updated to {app.AvailableVersion}.");
            return true;
        }

        app.Status = UpdateStatus.Failed;
        app.Message = WingetParser.DescribeExitCode(result.ExitCode);
        Log.Error($"{app.Name}: {app.Message}");
        return false;
    }

    /// <summary>Updates the given apps one at a time (parallel installers conflict with each other).</summary>
    public async Task<(int Ok, int Failed)> UpgradeManyAsync(IEnumerable<AppUpdate> apps, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        var list = apps.ToList();
        int ok = 0, failed = 0, done = 0;
        foreach (var app in list)
        {
            ct.ThrowIfCancellationRequested();
            if (await UpgradeAsync(app, ct)) ok++; else failed++;
            progress?.Report((++done, list.Count));
        }
        return (ok, failed);
    }
}
