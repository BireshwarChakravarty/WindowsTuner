using System.Diagnostics;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Startup;

public enum StartupLocation { UserRun, MachineRun, MachineRun32, UserStartupFolder, CommonStartupFolder }

public sealed class StartupItem : ObservableObject
{
    private bool _isEnabled;

    public required string Name { get; init; }
    public required string Command { get; init; }
    public required StartupLocation Location { get; init; }
    public string Publisher { get; init; } = "";
    public string FilePath { get; init; } = "";
    public bool FileExists { get; init; } = true;

    /// <summary>The value name used under StartupApproved (file name for startup-folder shortcuts).</summary>
    internal string ApprovedName { get; init; } = "";

    public string LocationText => Location switch
    {
        StartupLocation.UserRun => "Registry (you)",
        StartupLocation.MachineRun => "Registry (all users)",
        StartupLocation.MachineRun32 => "Registry (all users, 32-bit)",
        StartupLocation.UserStartupFolder => "Startup folder (you)",
        _ => "Startup folder (all users)",
    };

    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
}

/// <summary>Lists and toggles startup apps exactly like Task Manager does, so changes show up there too.</summary>
public sealed class StartupService(IRegistryAccess registry)
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public StartupService() : this(WindowsRegistry.Instance) { }

    private static (RegHive Hive, string Key) ApprovedKey(StartupLocation loc) => loc switch
    {
        StartupLocation.UserRun => (RegHive.HKCU, ApprovedRoot + @"\Run"),
        StartupLocation.MachineRun => (RegHive.HKLM, ApprovedRoot + @"\Run"),
        StartupLocation.MachineRun32 => (RegHive.HKLM, ApprovedRoot + @"\Run32"),
        StartupLocation.UserStartupFolder => (RegHive.HKCU, ApprovedRoot + @"\StartupFolder"),
        _ => (RegHive.HKLM, ApprovedRoot + @"\StartupFolder"),
    };

    public IReadOnlyList<StartupItem> GetItems()
    {
        var items = new List<StartupItem>();
        AddRunKey(items, RegHive.HKCU, RunKey, StartupLocation.UserRun);
        AddRunKey(items, RegHive.HKLM, RunKey, StartupLocation.MachineRun);
        if (Environment.Is64BitOperatingSystem) AddRunKey(items, RegHive.HKLM, Run32Key, StartupLocation.MachineRun32);
        AddFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLocation.UserStartupFolder);
        AddFolder(items, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupLocation.CommonStartupFolder);
        return items.OrderByDescending(i => i.IsEnabled).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void AddRunKey(List<StartupItem> items, RegHive hive, string key, StartupLocation loc)
    {
        var (aHive, aKey) = ApprovedKey(loc);
        foreach (var name in registry.GetValueNames(hive, key))
        {
            if (string.IsNullOrEmpty(name)) continue;
            var command = registry.GetValue(hive, key, name)?.ToString() ?? "";
            var exe = StartupApproved.ExtractExecutable(command);
            items.Add(new StartupItem
            {
                Name = name,
                Command = command,
                Location = loc,
                FilePath = exe,
                FileExists = SafeExists(exe),
                Publisher = GetPublisher(exe),
                ApprovedName = name,
                IsEnabled = StartupApproved.IsEnabled(registry.GetValue(aHive, aKey, name) as byte[]),
            });
        }
    }

    private void AddFolder(List<StartupItem> items, string folder, StartupLocation loc)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        var (aHive, aKey) = ApprovedKey(loc);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            items.Add(new StartupItem
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Command = file,
                Location = loc,
                FilePath = file,
                ApprovedName = fileName,
                IsEnabled = StartupApproved.IsEnabled(registry.GetValue(aHive, aKey, fileName) as byte[]),
            });
        }
    }

    public void SetEnabled(StartupItem item, bool enabled)
    {
        var (hive, key) = ApprovedKey(item.Location);
        registry.SetValue(hive, key, item.ApprovedName, StartupApproved.Create(enabled), RegKind.Binary);
        item.IsEnabled = enabled;
        Log.Success($"Startup: {item.Name} {(enabled ? "enabled" : "disabled")}.");
    }

    private static bool SafeExists(string path)
    {
        try { return path.Length == 0 || File.Exists(path) || !Path.IsPathRooted(path); }
        catch { return true; }
    }

    private static string GetPublisher(string exe)
    {
        try
        {
            if (exe.Length == 0 || !File.Exists(exe)) return "";
            var info = FileVersionInfo.GetVersionInfo(exe);
            return info.CompanyName?.Trim() ?? "";
        }
        catch { return ""; }
    }

    public static void OpenStartupSettings() => Shell.Open("ms-settings:startupapps");
}
