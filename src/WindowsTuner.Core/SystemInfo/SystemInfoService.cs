using System.Runtime.InteropServices;
using Microsoft.Win32;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.SystemInfo;

public static class SystemInfoService
{
    private const string NtKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public static int GetBuild()
    {
        var b = WindowsRegistry.Instance.GetValue(RegHive.HKLM, NtKey, "CurrentBuildNumber")?.ToString()
                ?? WindowsRegistry.Instance.GetValue(RegHive.HKLM, NtKey, "CurrentBuild")?.ToString();
        return int.TryParse(b, out var build) ? build : Environment.OSVersion.Version.Build;
    }

    public static string GetCpuName() =>
        (WindowsRegistry.Instance.GetValue(RegHive.HKLM, @"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") as string ?? "").Trim();

    /// <summary>Fast, local information (no network, no WMI) for the dashboard.</summary>
    public static SystemSnapshot GetBasic()
    {
        var reg = WindowsRegistry.Instance;
        var s = new SystemSnapshot
        {
            Build = GetBuild(),
            DisplayVersion = reg.GetValue(RegHive.HKLM, NtKey, "DisplayVersion") as string
                             ?? reg.GetValue(RegHive.HKLM, NtKey, "ReleaseId") as string ?? "",
            Ubr = reg.GetValue(RegHive.HKLM, NtKey, "UBR") is int ubr ? ubr : 0,
            CpuName = GetCpuName(),
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            IsAdmin = Elevation.IsAdministrator,
        };

        // ProductName still says "Windows 10" on Windows 11.
        var product = reg.GetValue(RegHive.HKLM, NtKey, "ProductName") as string ?? "Windows";
        s.OsName = s.IsWindows11 ? product.Replace("Windows 10", "Windows 11") : product;

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem))
        {
            s.TotalRamBytes = (long)mem.ullTotalPhys;
            s.AvailableRamBytes = (long)mem.ullAvailPhys;
            s.MemoryLoadPercent = (int)mem.dwMemoryLoad;
        }

        var sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        s.SystemDrive = sysDrive;
        try
        {
            var di = new DriveInfo(sysDrive);
            s.SystemDriveTotalBytes = di.TotalSize;
            s.SystemDriveFreeBytes = di.AvailableFreeSpace;
        }
        catch { }

        s.FastStartupEnabled = reg.GetValue(RegHive.HKLM, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled") is int hb && hb == 1
                               && reg.GetValue(RegHive.HKLM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") is not 0;
        s.PendingReboot = IsRebootPending();
        s.IsLaptop = HasBattery();
        return s;
    }

    public static bool IsRebootPending()
    {
        var reg = WindowsRegistry.Instance;
        return reg.KeyExists(RegHive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired")
               || reg.KeyExists(RegHive.HKLM, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
    }

    public static async Task<double> SampleCpuAsync(TimeSpan window)
    {
        if (!GetSystemTimes(out var idle1, out var kernel1, out var user1)) return 0;
        await Task.Delay(window);
        if (!GetSystemTimes(out var idle2, out var kernel2, out var user2)) return 0;
        var idle = idle2 - idle1;
        var total = (kernel2 - kernel1) + (user2 - user1); // kernel time includes idle time
        return total <= 0 ? 0 : Math.Clamp(100.0 * (total - idle) / total, 0, 100);
    }

    /// <summary>Media type of the disk holding Windows ("SSD"/"HDD"/"").</summary>
    public static async Task<string> GetSystemDriveMediaAsync()
    {
        var letter = (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:").TrimEnd(':', '\\');
        var r = await PowerShell.RunAsync(
            $"$p = Get-Partition -DriveLetter {letter} -ErrorAction SilentlyContinue | Get-Disk | Get-PhysicalDisk -ErrorAction SilentlyContinue; if ($p) {{ [string]$p.MediaType }}");
        var v = r.StdOut.Trim();
        return v switch
        {
            "SSD" or "4" => "SSD",
            "HDD" or "3" => "HDD",
            _ => "",
        };
    }

    private static bool HasBattery()
    {
        var status = new SYSTEM_POWER_STATUS();
        return GetSystemPowerStatus(ref status) && status.BatteryFlag != 128 && status.BatteryFlag != 255;
    }

    #region Native

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(ref SYSTEM_POWER_STATUS status);

    #endregion
}
