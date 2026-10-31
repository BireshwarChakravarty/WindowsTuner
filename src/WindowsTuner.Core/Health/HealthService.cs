using System.Text;
using System.Text.Json;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Health;

public sealed record DefenderStatus(bool RealTimeOn, int SignatureAgeDays, bool ServiceEnabled, string Product);

public static class HealthService
{
    /// <summary>System File Checker. Its output is UTF-16 when redirected.</summary>
    public static Task<ProcessResult> RunSfcAsync(CancellationToken ct = default)
    {
        Log.Info("Running System File Checker (sfc /scannow). This usually takes 5–15 minutes…");
        return ProcessRunner.RunAsync("sfc.exe", "/scannow", Log.Output, Encoding.Unicode, ct);
    }

    /// <summary>Repairs the Windows component store from Windows Update. Run before SFC if SFC can't fix files.</summary>
    public static Task<ProcessResult> RunDismRestoreHealthAsync(CancellationToken ct = default)
    {
        Log.Info("Running DISM /RestoreHealth (downloads clean files from Windows Update; 10–30 minutes)…");
        return ProcessRunner.RunAsync("dism.exe", "/Online /Cleanup-Image /RestoreHealth", Log.Output, ct: ct);
    }

    /// <summary>Online NTFS scan — finds file-system errors without a reboot.</summary>
    public static Task<ProcessResult> RunChkdskScanAsync(string drive = "C:", CancellationToken ct = default)
    {
        Log.Info($"Scanning {drive} for file-system errors (chkdsk /scan)…");
        return ProcessRunner.RunAsync("chkdsk.exe", $"{drive} /scan", Log.Output, ct: ct);
    }

    private static string MpCmdRun =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe");

    public static async Task<ProcessResult> UpdateDefenderSignaturesAsync(CancellationToken ct = default)
    {
        Log.Info("Updating Microsoft Defender virus definitions…");
        if (File.Exists(MpCmdRun))
            return await ProcessRunner.RunAsync(MpCmdRun, "-SignatureUpdate", Log.Output, ct: ct);
        return await PowerShell.RunAsync("Update-MpSignature", Log.Output, ct);
    }

    public static async Task<ProcessResult> RunDefenderQuickScanAsync(CancellationToken ct = default)
    {
        Log.Info("Running a Microsoft Defender quick scan…");
        if (File.Exists(MpCmdRun))
            return await ProcessRunner.RunAsync(MpCmdRun, "-Scan -ScanType 1", Log.Output, ct: ct);
        return await PowerShell.RunAsync("Start-MpScan -ScanType QuickScan", Log.Output, ct);
    }

    public static async Task<DefenderStatus?> GetDefenderStatusAsync(CancellationToken ct = default)
    {
        var r = await PowerShell.RunAsync(
            "$s = Get-MpComputerStatus -ErrorAction Stop; [pscustomobject]@{ RealTime=$s.RealTimeProtectionEnabled; Age=$s.AntivirusSignatureAge; Service=$s.AMServiceEnabled } | ConvertTo-Json -Compress",
            ct: ct);
        if (!r.Success) return null;
        try
        {
            var json = r.StdOut.Trim();
            var start = json.IndexOf('{');
            if (start < 0) return null;
            using var doc = JsonDocument.Parse(json[start..]);
            var root = doc.RootElement;
            return new DefenderStatus(
                root.TryGetProperty("RealTime", out var rt) && rt.ValueKind == JsonValueKind.True,
                root.TryGetProperty("Age", out var age) && age.TryGetInt32(out var a) ? a : -1,
                root.TryGetProperty("Service", out var sv) && sv.ValueKind == JsonValueKind.True,
                "Microsoft Defender");
        }
        catch { return null; }
    }

    public static async Task<IReadOnlyList<DiskHealth>> GetDiskHealthAsync(CancellationToken ct = default)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            $disks = @(Get-PhysicalDisk | ForEach-Object {
                $rc = $_ | Get-StorageReliabilityCounter
                [pscustomobject]@{
                    FriendlyName = $_.FriendlyName
                    MediaType = [string]$_.MediaType
                    BusType = [string]$_.BusType
                    HealthStatus = [string]$_.HealthStatus
                    Size = $_.Size
                    Wear = $rc.Wear
                    Temperature = $rc.Temperature
                    ReadErrorsUncorrected = $rc.ReadErrorsUncorrected
                }
            })
            ConvertTo-Json -InputObject $disks -Compress
            """;
        var r = await PowerShell.RunAsync(script, ct: ct);
        try { return DiskHealth.ParseJson(r.StdOut); }
        catch (Exception ex)
        {
            Log.Warn("Could not read disk health: " + ex.Message);
            return [];
        }
    }

    public static void ScheduleMemoryDiagnostic() => Shell.Start("mdsched.exe", "");
    public static void OpenReliabilityMonitor() => Shell.Start("perfmon.exe", "/rel");
    public static void OpenWindowsSecurity() => Shell.Open("windowsdefender:");
}
