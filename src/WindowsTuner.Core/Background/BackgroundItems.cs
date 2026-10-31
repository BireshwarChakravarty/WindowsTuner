using System.Diagnostics;
using System.Management;
using System.Text.Json;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Startup;

namespace WindowsTuner.Core.Background;

public sealed class ScheduledTaskInfo : ObservableObject
{
    private bool _isEnabled;
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string Author { get; init; } = "";
    public string Action { get; init; } = "";
    public string Triggers { get; init; } = "";
    public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }
    public bool IsProtected => Path.StartsWith(@"\WindowsTuner", StringComparison.OrdinalIgnoreCase);
    public string Hint =>
        Name.Contains("Update", StringComparison.OrdinalIgnoreCase) ? "Updater — app still updates when you open it" :
        Triggers.Contains("Logon", StringComparison.OrdinalIgnoreCase) ? "Runs at sign-in (hidden startup item)" :
        Triggers.Contains("Boot", StringComparison.OrdinalIgnoreCase) ? "Runs at boot" : "";
}

public enum ServiceRating { Keep, Updater, Optional }

public sealed class ServiceInfo : ObservableObject
{
    private string _startMode = "";
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string State { get; init; } = "";
    public string PathName { get; init; } = "";
    public string Company { get; init; } = "";
    public string Description { get; init; } = "";
    public string StartMode { get => _startMode; set => Set(ref _startMode, value); }
    public ServiceRating Rating => BackgroundService.Rate(Name, DisplayName, Company);
    public string RatingText => Rating switch
    {
        ServiceRating.Updater => "Updater — safe to set Manual",
        ServiceRating.Optional => "Optional",
        _ => "Keep (security / hardware)",
    };
}

/// <summary>Third-party scheduled tasks and services — the "hidden" startup items Task Manager doesn't show.</summary>
public static class BackgroundService
{
    public static IReadOnlyList<ScheduledTaskInfo> ParseTasksJson(string json)
    {
        var list = new List<ScheduledTaskInfo>();
        json = json.Trim();
        var start = json.IndexOfAny(['[', '{']);
        if (start < 0) return list;
        using var doc = JsonDocument.Parse(json[start..]);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        foreach (var e in items)
        {
            string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            if (S("TaskName").Length == 0) continue;
            list.Add(new ScheduledTaskInfo
            {
                Name = S("TaskName"), Path = S("TaskPath"), Author = S("Author"), Action = S("Action"), Triggers = S("Triggers"),
                IsEnabled = !S("State").Equals("Disabled", StringComparison.OrdinalIgnoreCase),
            });
        }
        return list;
    }

    public static async Task<IReadOnlyList<ScheduledTaskInfo>> GetThirdPartyTasksAsync(CancellationToken ct = default)
    {
        const string script = """
            $t = @(Get-ScheduledTask | Where-Object { $_.TaskPath -notlike '\Microsoft\*' } | ForEach-Object {
                [pscustomobject]@{
                    TaskName = $_.TaskName; TaskPath = $_.TaskPath; State = [string]$_.State; Author = [string]$_.Author
                    Action = (@($_.Actions) | ForEach-Object { ("" + $_.Execute + " " + $_.Arguments).Trim() }) -join '; '
                    Triggers = (@($_.Triggers) | ForEach-Object { $_.CimClass.CimClassName -replace '^MSFT_Task','' -replace 'Trigger$','' }) -join ', '
                }
            })
            ConvertTo-Json -InputObject $t -Compress
            """;
        var r = await PowerShell.RunAsync(script, ct: ct);
        return ParseTasksJson(r.StdOut).OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static async Task<bool> SetTaskEnabledAsync(ScheduledTaskInfo task, bool enabled, CancellationToken ct = default)
    {
        if (task.IsProtected) return false;
        var verb = enabled ? "Enable" : "Disable";
        var r = await PowerShell.RunAsync(
            $"{verb}-ScheduledTask -TaskPath {PowerShell.Quote(task.Path)} -TaskName {PowerShell.Quote(task.Name)} -ErrorAction Stop | Out-Null", ct: ct);
        if (r.Success)
        {
            task.IsEnabled = enabled;
            Log.Success($"Scheduled task {task.Path}{task.Name} {(enabled ? "enabled" : "disabled")}.");
        }
        else
        {
            // Some vendor tasks deny administrators; SYSTEM can still change them.
            var sys = await SystemRunner.RunAsync($"schtasks /Change /TN \"{task.Path}{task.Name}\" /{(enabled ? "ENABLE" : "DISABLE")}", TimeSpan.FromSeconds(60), ct);
            if (sys.Success) task.IsEnabled = enabled;
            else Log.Error($"Could not change {task.Name}: {r.StdErr.Trim()}");
        }
        return task.IsEnabled == enabled;
    }

    private static readonly string[] KeepWords =
    [
        "defender", "antivirus", "anti-virus", "security", "malware", "firewall", "norton", "mcafee", "kaspersky", "avast", "avg",
        "bitdefender", "eset", "sophos", "trend micro", "webroot", "crowdstrike", "sentinel", "audio", "realtek", "nvidia display",
        "intel", "amd ", "synaptics", "elan", "bluetooth", "wi-fi", "wireless", "dolby", "thunderbolt", "touch", "fingerprint",
        "dell", "lenovo", "hp ", "asus", "vpn", "backup", "printer", "spooler", "vmware", "virtualbox", "hyper-v",
    ];

    public static ServiceRating Rate(string name, string displayName, string company)
    {
        var text = $"{name} {displayName} {company}".ToLowerInvariant();
        if (text.Contains("update") || text.Contains("updater") || name.Equals("gupdate", StringComparison.OrdinalIgnoreCase)
            || name.Equals("gupdatem", StringComparison.OrdinalIgnoreCase) || text.Contains("maintenance service"))
            return ServiceRating.Updater;
        return KeepWords.Any(text.Contains) ? ServiceRating.Keep : ServiceRating.Optional;
    }

    public static Task<IReadOnlyList<ServiceInfo>> GetThirdPartyServicesAsync(CancellationToken ct = default) => Task.Run<IReadOnlyList<ServiceInfo>>(() =>
    {
        var list = new List<ServiceInfo>();
        using var searcher = new ManagementObjectSearcher("SELECT Name, DisplayName, StartMode, State, PathName, Description FROM Win32_Service");
        foreach (var o in searcher.Get())
        {
            ct.ThrowIfCancellationRequested();
            using (o)
            {
                var path = o["PathName"] as string ?? "";
                var exe = StartupApproved.ExtractExecutable(path);
                if (exe.EndsWith("svchost.exe", StringComparison.OrdinalIgnoreCase)) continue; // Windows-hosted
                var company = "";
                try { if (File.Exists(exe)) company = FileVersionInfo.GetVersionInfo(exe).CompanyName?.Trim() ?? ""; } catch { }
                if (company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                if (exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase) && company.Length == 0) continue;
                list.Add(new ServiceInfo
                {
                    Name = o["Name"] as string ?? "",
                    DisplayName = o["DisplayName"] as string ?? "",
                    StartMode = NormalizeStartMode(o["StartMode"] as string),
                    State = o["State"] as string ?? "",
                    PathName = path,
                    Company = company,
                    Description = o["Description"] as string ?? "",
                });
            }
        }
        return list.OrderBy(s => s.Rating).ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }, ct);

    public static string NormalizeStartMode(string? wmi) => wmi switch
    {
        "Auto" => "Automatic",
        "Manual" => "Manual",
        "Disabled" => "Disabled",
        _ => wmi ?? "",
    };

    private static string ScArg(string mode) => mode switch
    {
        "Automatic" => "auto",
        "Disabled" => "disabled",
        _ => "demand",
    };

    private static string JournalPath => Path.Combine(AppPaths.Root, "services-journal.json");

    public static Dictionary<string, string> LoadJournal() =>
        JsonFile.Read<Dictionary<string, string>>(JournalPath) ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Changes a service's start type; the original is remembered so "Restore all" can undo it.</summary>
    public static async Task<bool> SetStartModeAsync(ServiceInfo svc, string mode, CancellationToken ct = default)
    {
        var journal = LoadJournal();
        if (!journal.ContainsKey(svc.Name)) journal[svc.Name] = svc.StartMode;
        var r = await ProcessRunner.RunAsync("sc.exe", $"config \"{svc.Name}\" start= {ScArg(mode)}", Log.Output, ct: ct);
        if (!r.Success)
        {
            // Some vendors lock their service key against administrators; SYSTEM can still change it.
            r = await SystemRunner.RunAsync($"sc.exe config \"{svc.Name}\" start= {ScArg(mode)}", TimeSpan.FromSeconds(60), ct);
        }
        if (!r.Success)
        {
            Log.Error($"Could not change {svc.DisplayName}: {r.Combined.Trim()}");
            return false;
        }
        svc.StartMode = mode;
        if (journal.TryGetValue(svc.Name, out var original) && original == mode) journal.Remove(svc.Name);
        JsonFile.Write(JournalPath, journal);
        Log.Success($"{svc.DisplayName}: start type set to {mode}.");
        return true;
    }

    public static async Task<int> RestoreAllAsync(IEnumerable<ServiceInfo> services, CancellationToken ct = default)
    {
        var journal = LoadJournal();
        var byName = services.ToDictionary(s => s.Name, StringComparer.OrdinalIgnoreCase);
        var restored = 0;
        foreach (var (name, mode) in journal.ToList())
        {
            if (byName.TryGetValue(name, out var svc))
            {
                if (await SetStartModeAsync(svc, mode, ct)) restored++;
            }
            else
            {
                await ProcessRunner.RunAsync("sc.exe", $"config \"{name}\" start= {ScArg(mode)}", Log.Output, ct: ct);
                restored++;
            }
        }
        JsonFile.Write(JournalPath, new Dictionary<string, string>());
        return restored;
    }
}
