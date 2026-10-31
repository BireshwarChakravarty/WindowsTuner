using System.Globalization;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Startup;

namespace WindowsTuner.Core.Programs;

public sealed class InstalledProgram : ObservableObject
{
    private string _status = "";

    public required string Name { get; init; }
    public string Publisher { get; init; } = "";
    public string Version { get; init; } = "";
    public DateTime? InstallDate { get; init; }
    public long SizeBytes { get; init; }
    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public string Scope { get; init; } = "";

    public string Size => SizeBytes > 0 ? Format.Bytes(SizeBytes) : "";
    public string Installed => InstallDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    public int? AgeDays => InstallDate is { } d ? (int)(DateTime.Today - d).TotalDays : null;
    public bool CanUninstall => UninstallString.Length > 0;
    public string Status { get => _status; set => Set(ref _status, value); }
}

/// <summary>Reads Programs and Features from the registry (all three Uninstall locations).</summary>
public sealed class InstalledProgramsService(IRegistryAccess registry)
{
    private const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string Uninstall32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public InstalledProgramsService() : this(WindowsRegistry.Instance) { }

    public IReadOnlyList<InstalledProgram> GetPrograms()
    {
        var list = new List<InstalledProgram>();
        Read(list, RegHive.HKLM, Uninstall, "All users");
        Read(list, RegHive.HKLM, Uninstall32, "All users (32-bit)");
        Read(list, RegHive.HKCU, Uninstall, "You");
        return list
            .GroupBy(p => (p.Name.ToLowerInvariant(), p.Version))
            .Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Read(List<InstalledProgram> list, RegHive hive, string root, string scope)
    {
        foreach (var sub in registry.GetSubKeyNames(hive, root))
        {
            var key = root + "\\" + sub;
            var name = Str(hive, key, "DisplayName");
            if (string.IsNullOrWhiteSpace(name)) continue;
            // Hidden components and patches/updates of other products.
            if (registry.GetValue(hive, key, "SystemComponent") is int sc && sc == 1) continue;
            if (Str(hive, key, "ParentKeyName").Length > 0 || Str(hive, key, "ReleaseType") is "Update" or "Hotfix" or "Security Update") continue;

            var sizeKb = registry.GetValue(hive, key, "EstimatedSize") is int kb ? kb : 0;
            list.Add(new InstalledProgram
            {
                Name = name.Trim(),
                Publisher = Str(hive, key, "Publisher"),
                Version = Str(hive, key, "DisplayVersion"),
                InstallDate = ParseInstallDate(Str(hive, key, "InstallDate")),
                SizeBytes = (long)Math.Max(0, sizeKb) * 1024,
                UninstallString = Str(hive, key, "UninstallString"),
                QuietUninstallString = Str(hive, key, "QuietUninstallString"),
                InstallLocation = Str(hive, key, "InstallLocation"),
                Scope = scope,
            });
        }
    }

    private string Str(RegHive hive, string key, string name) => registry.GetValue(hive, key, name)?.ToString()?.Trim() ?? "";

    public static DateTime? ParseInstallDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        string[] formats = ["yyyyMMdd", "yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "dd.MM.yyyy"];
        return DateTime.TryParseExact(v, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d.Year > 1990
            ? d
            : null;
    }

    /// <summary>Splits an UninstallString into executable and arguments.</summary>
    public static (string File, string Args) SplitCommand(string command)
    {
        var exe = StartupApproved.ExtractExecutable(command);
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        var rest = expanded.StartsWith('"') ? expanded[(exe.Length + 2)..] : expanded.Length > exe.Length ? expanded[exe.Length..] : "";
        return (exe, rest.Trim());
    }

    /// <summary>Runs the program's own uninstaller (interactive, so you see exactly what it removes).</summary>
    public static async Task<bool> UninstallAsync(InstalledProgram program, CancellationToken ct = default)
    {
        if (!program.CanUninstall) return false;
        var (file, args) = SplitCommand(program.UninstallString);
        // MsiExec /I (repair/modify) → /X (remove).
        if (Path.GetFileName(file).Equals("MsiExec.exe", StringComparison.OrdinalIgnoreCase))
            args = args.Replace("/I{", "/X{", StringComparison.OrdinalIgnoreCase).Replace("/I {", "/X {", StringComparison.OrdinalIgnoreCase);
        Log.Info($"Starting uninstaller for {program.Name}…");
        program.Status = "Uninstaller running…";
        var r = await ProcessRunner.RunAsync(file, args, Log.Output, ct: ct);
        program.Status = r.Success ? "Uninstalled (or uninstaller closed)" : $"Uninstaller exited with {r.ExitCode}";
        return r.Success;
    }
}
