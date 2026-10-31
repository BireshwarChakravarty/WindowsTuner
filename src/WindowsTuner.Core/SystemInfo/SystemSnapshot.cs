namespace WindowsTuner.Core.SystemInfo;

public sealed class SystemSnapshot
{
    public string OsName { get; set; } = "";
    public string DisplayVersion { get; set; } = "";
    public int Build { get; set; }
    public int Ubr { get; set; }
    public bool IsWindows11 => Build >= 22000;
    public string ComputerName { get; set; } = Environment.MachineName;
    public string CpuName { get; set; } = "";
    public int LogicalCpus { get; set; } = Environment.ProcessorCount;
    public long TotalRamBytes { get; set; }
    public long AvailableRamBytes { get; set; }
    public int MemoryLoadPercent { get; set; }
    public double CpuLoadPercent { get; set; }
    public TimeSpan Uptime { get; set; }
    public bool FastStartupEnabled { get; set; }
    public bool PendingReboot { get; set; }
    public string SystemDrive { get; set; } = "C:";
    public long SystemDriveTotalBytes { get; set; }
    public long SystemDriveFreeBytes { get; set; }
    /// <summary>"SSD", "HDD" or "" when unknown.</summary>
    public string SystemDriveMedia { get; set; } = "";
    public int EnabledStartupApps { get; set; } = -1;
    public int AppUpdatesAvailable { get; set; } = -1;
    public int WindowsUpdatesAvailable { get; set; } = -1;
    public int ProblemDevices { get; set; } = -1;
    public int DefenderSignatureAgeDays { get; set; } = -1;
    public bool? DefenderRealTimeOn { get; set; }
    public bool AnyDiskUnhealthy { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsLaptop { get; set; }

    public double SystemDriveFreePercent =>
        SystemDriveTotalBytes > 0 ? 100.0 * SystemDriveFreeBytes / SystemDriveTotalBytes : 100;

    public string OsDisplay => $"{OsName} {DisplayVersion} (build {Build}.{Ubr})".Trim();
}
