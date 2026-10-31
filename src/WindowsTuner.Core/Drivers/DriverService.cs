using System.Management;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Drivers;

public sealed class HardwareSummary
{
    public List<string> Gpus { get; } = [];
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string CpuName { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public DateTime? BiosDate { get; set; }
}

public sealed class DriverService
{
    public Task<IReadOnlyList<DriverInfo>> GetInstalledDriversAsync(CancellationToken ct = default) => Task.Run<IReadOnlyList<DriverInfo>>(() =>
    {
        var list = new List<DriverInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceName, DeviceClass, DriverProviderName, DriverVersion, DriverDate, InfName, IsSigned FROM Win32_PnPSignedDriver");
        foreach (var o in searcher.Get())
        {
            ct.ThrowIfCancellationRequested();
            using (o)
            {
                var name = o["DeviceName"] as string;
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new DriverInfo(
                    name,
                    o["DeviceClass"] as string ?? "",
                    o["DriverProviderName"] as string ?? "",
                    o["DriverVersion"] as string ?? "",
                    DriverInfo.ParseCimDate(o["DriverDate"] as string),
                    o["InfName"] as string ?? "",
                    o["IsSigned"] is bool b && b));
            }
        }
        return list.OrderBy(d => d.DeviceClass).ThenBy(d => d.DeviceName).ToList();
    }, ct);

    public Task<IReadOnlyList<ProblemDevice>> GetProblemDevicesAsync(CancellationToken ct = default) => Task.Run<IReadOnlyList<ProblemDevice>>(() =>
    {
        var list = new List<ProblemDevice>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, PNPClass, ConfigManagerErrorCode, DeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
        foreach (var o in searcher.Get())
        {
            using (o)
            {
                var code = Convert.ToInt32(o["ConfigManagerErrorCode"] ?? 0);
                if (code is 0 or 45) continue; // 45 = device simply not plugged in
                list.Add(new ProblemDevice(o["Name"] as string ?? "(unknown device)", o["PNPClass"] as string ?? "", code, o["DeviceID"] as string ?? ""));
            }
        }
        return list;
    }, ct);

    public Task<HardwareSummary> GetHardwareAsync() => Task.Run(() =>
    {
        var hw = new HardwareSummary();
        try
        {
            using var gpu = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var o in gpu.Get()) using (o) if (o["Name"] is string n) hw.Gpus.Add(n);
        }
        catch (Exception ex) { Log.Warn("GPU query failed: " + ex.Message); }
        try
        {
            using var cs = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem");
            foreach (var o in cs.Get()) using (o)
            {
                hw.Manufacturer = (o["Manufacturer"] as string ?? "").Trim();
                hw.Model = (o["Model"] as string ?? "").Trim();
            }
        }
        catch (Exception ex) { Log.Warn("System query failed: " + ex.Message); }
        try
        {
            using var bios = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            foreach (var o in bios.Get()) using (o)
            {
                hw.BiosVersion = o["SMBIOSBIOSVersion"] as string ?? "";
                hw.BiosDate = DriverInfo.ParseCimDate(o["ReleaseDate"] as string);
            }
        }
        catch { }
        hw.CpuName = SystemInfo.SystemInfoService.GetCpuName();
        return hw;
    });

    /// <summary>Exports all third-party drivers so they can be restored if an update misbehaves.</summary>
    public static async Task<string?> BackupDriversAsync(CancellationToken ct = default)
    {
        var dest = Path.Combine(AppPaths.Backups, $"Drivers-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(dest);
        Log.Info($"Exporting third-party drivers to {dest} …");
        var r = await ProcessRunner.RunAsync("dism.exe", $"/online /export-driver /destination:\"{dest}\"", Log.Output, ct: ct);
        if (r.Success)
        {
            Log.Success($"Drivers backed up to {dest}. Restore with: pnputil /add-driver \"{dest}\\*.inf\" /subdirs /install");
            return dest;
        }
        Log.Error("Driver backup failed: " + r.Combined.Trim());
        return null;
    }

    /// <summary>Re-enumerates devices ("Scan for hardware changes" in Device Manager).</summary>
    public static Task<ProcessResult> ScanForHardwareChangesAsync(CancellationToken ct = default) =>
        ProcessRunner.RunAsync("pnputil.exe", "/scan-devices", Log.Output, ct: ct);

    public static void OpenDeviceManager() => Shell.Start("devmgmt.msc", "");
}
