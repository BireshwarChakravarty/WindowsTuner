using System.Text.Json;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Diagnostics;

public sealed record CrashingApp(string Name, int Count);

/// <summary>What the Windows event logs say about stability in the last 30 days.</summary>
public sealed class EventLogReport
{
    public int UnexpectedShutdowns { get; set; }
    public int BlueScreens { get; set; }
    public int DiskErrors { get; set; }
    public int FileSystemErrors { get; set; }
    public int HardwareErrors { get; set; }
    public int AppCrashes { get; set; }
    public double? LastBootSeconds { get; set; }
    public List<CrashingApp> TopCrashingApps { get; set; } = [];

    public const string Script = """
        $ErrorActionPreference = 'SilentlyContinue'
        $since = (Get-Date).AddDays(-30)
        function C($f) { @(Get-WinEvent -FilterHashtable $f -ErrorAction SilentlyContinue).Count }
        $o = [ordered]@{}
        $o.UnexpectedShutdowns = C @{ LogName='System'; ProviderName='Microsoft-Windows-Kernel-Power'; Id=41; StartTime=$since }
        $o.BlueScreens = C @{ LogName='System'; ProviderName='Microsoft-Windows-WER-SystemErrorReporting'; Id=1001; StartTime=$since }
        $o.DiskErrors = C @{ LogName='System'; ProviderName='disk'; Id=7,51,153; StartTime=$since }
        $o.FileSystemErrors = C @{ LogName='System'; ProviderName='Microsoft-Windows-Ntfs','Ntfs'; Id=55,98; StartTime=$since }
        $o.HardwareErrors = C @{ LogName='System'; ProviderName='Microsoft-Windows-WHEA-Logger'; Id=1,17,18,19,47; StartTime=$since }
        $crashes = @(Get-WinEvent -FilterHashtable @{ LogName='Application'; ProviderName='Application Error'; Id=1000; StartTime=$since } -ErrorAction SilentlyContinue)
        $o.AppCrashes = $crashes.Count
        $o.TopCrashingApps = @($crashes | ForEach-Object { $_.Properties[0].Value } | Group-Object | Sort-Object Count -Descending | Select-Object -First 5 | ForEach-Object { [pscustomobject]@{ Name = $_.Name; Count = $_.Count } })
        $boot = Get-WinEvent -FilterHashtable @{ LogName='Microsoft-Windows-Diagnostics-Performance/Operational'; Id=100 } -MaxEvents 1 -ErrorAction SilentlyContinue
        if ($boot) { $o.LastBootMs = (([xml]$boot.ToXml()).Event.EventData.Data | Where-Object Name -eq 'BootTime').'#text' }
        $o | ConvertTo-Json -Compress -Depth 4
        """;

    public static EventLogReport Parse(string json)
    {
        var report = new EventLogReport();
        var start = json.IndexOf('{');
        if (start < 0) return report;
        using var doc = JsonDocument.Parse(json[start..]);
        var r = doc.RootElement;
        int I(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32()
            : r.TryGetProperty(n, out v) && v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var x) ? x : 0;
        report.UnexpectedShutdowns = I("UnexpectedShutdowns");
        report.BlueScreens = I("BlueScreens");
        report.DiskErrors = I("DiskErrors");
        report.FileSystemErrors = I("FileSystemErrors");
        report.HardwareErrors = I("HardwareErrors");
        report.AppCrashes = I("AppCrashes");
        var bootMs = I("LastBootMs");
        if (bootMs > 0) report.LastBootSeconds = bootMs / 1000.0;
        if (r.TryGetProperty("TopCrashingApps", out var apps))
        {
            var items = apps.ValueKind == JsonValueKind.Array ? apps.EnumerateArray().ToList() : apps.ValueKind == JsonValueKind.Object ? [apps] : [];
            foreach (var a in items)
            {
                var name = a.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";
                var count = a.TryGetProperty("Count", out var c) && c.TryGetInt32(out var ci) ? ci : 0;
                if (name.Length > 0) report.TopCrashingApps.Add(new CrashingApp(name, count));
            }
        }
        return report;
    }

    public static async Task<EventLogReport?> CollectAsync(CancellationToken ct = default)
    {
        var r = await PowerShell.RunAsync(Script, ct: ct);
        try { return Parse(r.StdOut); }
        catch (Exception ex)
        {
            Log.Warn("Event log analysis failed: " + ex.Message);
            return null;
        }
    }

    /// <summary>Plain-English findings.</summary>
    public IEnumerable<(bool Problem, string Text)> Findings()
    {
        yield return BlueScreens > 0
            ? (true, $"{BlueScreens} blue screen(s) in 30 days — usually a driver or RAM. Update drivers, then run the memory test.")
            : (false, "No blue screens in 30 days.");
        yield return UnexpectedShutdowns > 0
            ? (true, $"{UnexpectedShutdowns} unexpected shutdown(s) — power cuts, holding the power button, overheating or a failing power supply.")
            : (false, "No unexpected shutdowns.");
        yield return DiskErrors + FileSystemErrors > 0
            ? (true, $"{DiskErrors} disk and {FileSystemErrors} file-system error(s) — back up now and check disk health.")
            : (false, "No disk errors.");
        yield return HardwareErrors > 0
            ? (true, $"{HardwareErrors} hardware (WHEA) error(s) — CPU, RAM or PCIe problems; often overclocking or overheating.")
            : (false, "No hardware errors.");
        yield return AppCrashes > 0
            ? (AppCrashes > 10, $"{AppCrashes} app crash(es)" + (TopCrashingApps.Count > 0 ? ": " + string.Join(", ", TopCrashingApps.Select(a => $"{a.Name} ×{a.Count}")) : "") + ". Update or reinstall those apps.")
            : (false, "No app crashes.");
        if (LastBootSeconds is { } s)
            yield return s > 60 ? (true, $"Last boot took {s:0} s — trim startup apps and background tasks.") : (false, $"Last boot took {s:0} s.");
    }
}
