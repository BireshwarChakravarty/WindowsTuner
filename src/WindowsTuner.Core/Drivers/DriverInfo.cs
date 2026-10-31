using System.Globalization;

namespace WindowsTuner.Core.Drivers;

public sealed record DriverInfo(
    string DeviceName,
    string DeviceClass,
    string Provider,
    string Version,
    DateTime? Date,
    string InfName,
    bool IsSigned)
{
    public int? AgeYears => Date is { } d ? (int)((DateTime.Today - d).TotalDays / 365.25) : null;

    /// <summary>
    /// Windows' own in-box drivers carry the placeholder date 21 June 2006 regardless of their real age, so an
    /// old date only means something for third-party drivers.
    /// </summary>
    public bool IsInboxMicrosoftDriver =>
        Provider.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) ||
        (Date is { Year: 2006, Month: 6, Day: 21 });

    public bool LooksOutdated(int years = 3) => !IsInboxMicrosoftDriver && AgeYears >= years;

    public string DateText => Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>Parses a WMI CIM_DATETIME such as "20230615000000.******+000".</summary>
    public static DateTime? ParseCimDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8) return null;
        return DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }
}

public sealed record ProblemDevice(string Name, string DeviceClass, int ErrorCode, string DeviceId)
{
    public string Problem => DescribeError(ErrorCode);

    /// <summary>Device Manager error codes (https://learn.microsoft.com/windows-hardware/drivers/install/device-manager-error-messages).</summary>
    public static string DescribeError(int code) => code switch
    {
        1 => "Not configured correctly (Code 1) — update the driver",
        3 => "Driver may be corrupted or low on memory (Code 3)",
        10 => "Device cannot start (Code 10) — update or reinstall the driver",
        12 => "Not enough free resources (Code 12)",
        14 => "Restart required (Code 14)",
        18 => "Reinstall the drivers (Code 18)",
        19 => "Registry configuration damaged (Code 19)",
        21 => "Windows is removing this device (Code 21)",
        22 => "Disabled (Code 22)",
        24 => "Not present or not working properly (Code 24)",
        28 => "Drivers are not installed (Code 28)",
        29 => "Disabled in firmware (Code 29)",
        31 => "Not working properly — driver failed to load (Code 31)",
        32 => "Driver service disabled (Code 32)",
        37 => "Driver failed to initialize (Code 37)",
        39 => "Driver is corrupted or missing (Code 39)",
        43 => "Device reported a problem and was stopped (Code 43)",
        45 => "Not connected (Code 45)",
        48 => "Driver blocked because of known compatibility issues (Code 48)",
        52 => "Driver signature cannot be verified (Code 52)",
        _ => $"Problem code {code}",
    };
}

public sealed record VendorTool(string Name, string Reason, string Url);

/// <summary>
/// Windows Update carries drivers but often not the newest GPU and chipset releases. These are the official,
/// vendor-provided updaters — never use third-party "driver updater" sites or tools.
/// </summary>
public static class VendorToolAdvisor
{
    public static IReadOnlyList<VendorTool> Recommend(IEnumerable<string> gpuNames, string? systemManufacturer, string? cpuName)
    {
        var tools = new List<VendorTool>();
        var gpus = gpuNames.Where(g => !string.IsNullOrWhiteSpace(g)).ToList();

        if (gpus.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || g.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || g.Contains("RTX", StringComparison.OrdinalIgnoreCase)))
            tools.Add(new("NVIDIA App", "NVIDIA graphics detected — newest Game Ready / Studio drivers", "https://www.nvidia.com/en-us/software/nvidia-app/"));
        if (gpus.Any(g => g.Contains("AMD", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon", StringComparison.OrdinalIgnoreCase)))
            tools.Add(new("AMD Software: Adrenalin Edition", "AMD Radeon graphics detected", "https://www.amd.com/en/support/download/drivers.html"));
        if (gpus.Any(g => g.Contains("Intel", StringComparison.OrdinalIgnoreCase)) ||
            (cpuName?.Contains("Intel", StringComparison.OrdinalIgnoreCase) ?? false))
            tools.Add(new("Intel Driver & Support Assistant", "Intel graphics / Wi-Fi / Bluetooth / chipset drivers", "https://www.intel.com/content/www/us/en/support/detect.html"));
        if (cpuName?.Contains("AMD", StringComparison.OrdinalIgnoreCase) ?? false)
            tools.Add(new("AMD Chipset Drivers", "AMD Ryzen CPU detected — chipset and power-plan drivers", "https://www.amd.com/en/support/download/drivers.html"));

        var m = systemManufacturer ?? "";
        if (m.Contains("Dell", StringComparison.OrdinalIgnoreCase) || m.Contains("Alienware", StringComparison.OrdinalIgnoreCase))
            tools.Add(new("Dell Command | Update / SupportAssist", "Dell PC — BIOS, firmware and Dell-tested drivers", "https://www.dell.com/support/home"));
        else if (m.Contains("Lenovo", StringComparison.OrdinalIgnoreCase))
            tools.Add(new("Lenovo Vantage / System Update", "Lenovo PC — BIOS, firmware and Lenovo-tested drivers", "https://support.lenovo.com/solutions/ht003029"));
        else if (m.Contains("HP", StringComparison.OrdinalIgnoreCase) || m.Contains("Hewlett", StringComparison.OrdinalIgnoreCase))
            tools.Add(new("HP Support Assistant", "HP PC — BIOS, firmware and HP-tested drivers", "https://support.hp.com/us-en/help/hp-support-assistant"));
        else if (m.Contains("ASUS", StringComparison.OrdinalIgnoreCase))
            tools.Add(new("MyASUS", "ASUS PC — BIOS and ASUS drivers", "https://www.asus.com/support/download-center/"));
        else if (m.Contains("Acer", StringComparison.OrdinalIgnoreCase))
            tools.Add(new("Acer Care Center", "Acer PC — BIOS and Acer drivers", "https://www.acer.com/us-en/support"));
        else if (m.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase) || m.Contains("MSI", StringComparison.OrdinalIgnoreCase))
            tools.Add(new("MSI Center", "MSI PC / motherboard — BIOS and drivers", "https://www.msi.com/support"));
        else if (m.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
            tools.Add(new("Windows Update (Surface)", "Surface firmware and drivers are delivered through Windows Update", "https://support.microsoft.com/surface"));
        else if (m.Length > 0 && !m.Contains("To Be Filled", StringComparison.OrdinalIgnoreCase) && !m.Contains("System manufacturer", StringComparison.OrdinalIgnoreCase))
            tools.Add(new($"{m} support site", "Check your PC/motherboard maker's support page for BIOS and chipset updates", "https://www.google.com/search?q=" + Uri.EscapeDataString(m + " drivers support")));
        else
            tools.Add(new("Motherboard maker's support page", "Custom-built PC — BIOS and chipset drivers come from the motherboard vendor", "https://www.google.com/search?q=motherboard+drivers+support"));

        return tools;
    }
}
