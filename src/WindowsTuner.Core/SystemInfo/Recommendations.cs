using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.SystemInfo;

public enum Severity { Good, Info, Warning, Critical }

/// <summary>Navigation target in the UI that fixes the issue.</summary>
public enum FixPage { None, Updates, Drivers, Cleanup, Startup, Tweaks, Performance, Health, Apps, TuneUp, Restart }

public sealed record Recommendation(Severity Severity, string Title, string Detail, FixPage Fix = FixPage.None)
{
    public string Icon => Severity switch
    {
        Severity.Critical => "⛔",
        Severity.Warning => "⚠",
        Severity.Info => "ℹ",
        _ => "✔",
    };

    public string FixText => Fix switch
    {
        FixPage.None => "",
        FixPage.Restart => "Restart now",
        FixPage.TuneUp => "Run Tune-up",
        _ => "Fix it →",
    };
}

/// <summary>Turns a system snapshot into prioritised, plain-English advice. Pure logic (unit-tested).</summary>
public static class RecommendationEngine
{
    public static IReadOnlyList<Recommendation> Evaluate(SystemSnapshot s)
    {
        var list = new List<Recommendation>();

        if (!s.IsAdmin)
            list.Add(new(Severity.Warning, "Run WindowsTuner as administrator",
                "Most repairs, updates and cleanups need administrator rights. Right-click WindowsTuner and choose \"Run as administrator\"."));

        // Windows 10 reached end of support on 14 Oct 2025. Consumer Extended Security Updates run to 12 Oct 2027.
        if (s.Build is > 0 and < 22000)
            list.Add(new(Severity.Critical, "Windows 10 is out of mainstream support",
                "Windows 10 stopped getting free updates on 14 Oct 2025. Enroll in Extended Security Updates (Settings → Windows Update) to keep getting security fixes until Oct 2027, or upgrade to Windows 11 if your PC supports it.",
                FixPage.Updates));
        else if (s.IsWindows11 && s.Build < 26100)
            list.Add(new(Severity.Warning, $"Windows 11 {s.DisplayVersion} is an older release",
                "Windows 11 23H2 and older no longer receive security updates on Home and Pro editions. Install the latest feature update (24H2 or newer) from Windows Update.",
                FixPage.Updates));

        if (s.PendingReboot)
            list.Add(new(Severity.Critical, "A restart is waiting to finish updates",
                "Windows has installed updates that only complete after a restart. Until then the PC runs slower and other updates can't install.",
                FixPage.Restart));

        if (s.Uptime.TotalDays >= 7)
            list.Add(new(Severity.Warning, $"Your PC hasn't fully restarted in {(int)s.Uptime.TotalDays} days",
                s.FastStartupEnabled
                    ? "Fast Startup is on, so \"Shut down\" doesn't really reset Windows. Use Restart (or turn off Fast Startup on the Tweaks page)."
                    : "Restarting clears memory leaks and lets pending updates finish.",
                FixPage.Restart));

        if (s.SystemDriveTotalBytes > 0)
        {
            if (s.SystemDriveFreePercent < 10 || s.SystemDriveFreeBytes < 15L * 1024 * 1024 * 1024)
                list.Add(new(Severity.Critical, $"Drive {s.SystemDrive} is almost full ({Format.Bytes(s.SystemDriveFreeBytes)} free)",
                    "Windows needs free space for updates, the page file and temporary files. A full drive makes everything slow and updates fail.",
                    FixPage.Cleanup));
            else if (s.SystemDriveFreePercent < 20)
                list.Add(new(Severity.Warning, $"Drive {s.SystemDrive} is getting full ({s.SystemDriveFreePercent:0}% free)",
                    "Clean up junk files and consider removing apps you don't use.", FixPage.Cleanup));
        }

        if (s.SystemDriveMedia == "HDD")
            list.Add(new(Severity.Warning, "Windows is on a mechanical hard disk",
                "This is the #1 cause of a slow PC. Moving to an SSD (from ~$40) makes boot, updates and app launches 5–10× faster — no software can match that.",
                FixPage.Performance));

        if (s.TotalRamBytes > 0)
        {
            var gb = s.TotalRamBytes / (1024.0 * 1024 * 1024);
            if (gb < 7.5)
                list.Add(new(Severity.Warning, $"Only {gb:0} GB of RAM",
                    "Windows 11 with a modern browser really wants 8 GB or more. Keep few startup apps and browser tabs, or add RAM if your PC allows it.",
                    FixPage.Startup));
            if (s.MemoryLoadPercent >= 85)
                list.Add(new(Severity.Warning, $"Memory is {s.MemoryLoadPercent}% used",
                    "When RAM is full Windows swaps to disk and everything stutters. See what's using it on the Performance page.",
                    FixPage.Performance));
        }

        if (s.CpuLoadPercent >= 80)
            list.Add(new(Severity.Info, $"CPU is busy ({s.CpuLoadPercent:0}%)",
                "After a long time switched off Windows catches up on updates, virus scans and indexing for a while. Check the Performance page to see what's running.",
                FixPage.Performance));

        if (s.EnabledStartupApps > 10)
            list.Add(new(Severity.Warning, $"{s.EnabledStartupApps} apps start with Windows",
                "Every startup app slows sign-in and uses memory all day. Disable the ones you don't need right away.",
                FixPage.Startup));

        if (s.AppUpdatesAvailable > 0)
            list.Add(new(Severity.Warning, $"{s.AppUpdatesAvailable} app update(s) available",
                "Outdated apps are the most common way PCs get infected. Update them all in one click.", FixPage.Updates));

        if (s.WindowsUpdatesAvailable > 0)
            list.Add(new(Severity.Warning, $"{s.WindowsUpdatesAvailable} Windows / driver update(s) available",
                "Install security and driver updates first — they also fix many performance bugs.", FixPage.Updates));

        if (s.ProblemDevices > 0)
            list.Add(new(Severity.Warning, $"{s.ProblemDevices} device(s) have driver problems",
                "Check the Drivers page for details and fixes.", FixPage.Drivers));

        if (s.DefenderRealTimeOn == false)
            list.Add(new(Severity.Critical, "Real-time virus protection is off",
                "Turn Microsoft Defender real-time protection back on in Windows Security (unless another antivirus is installed).",
                FixPage.Health));
        if (s.DefenderSignatureAgeDays > 3)
            list.Add(new(Severity.Warning, $"Virus definitions are {s.DefenderSignatureAgeDays} days old",
                "Update Microsoft Defender definitions on the Health page.", FixPage.Health));

        if (s.AnyDiskUnhealthy)
            list.Add(new(Severity.Critical, "A disk reports a health problem",
                "Back up your files now. A failing disk also causes freezes and extreme slowness.", FixPage.Health));

        if (list.Count == 0 || list.All(r => r.Severity == Severity.Info))
            list.Add(new(Severity.Good, "Looking good", "No problems found. Run a Tune-up now and then to keep it that way.", FixPage.TuneUp));

        return list.OrderByDescending(r => r.Severity).ToList();
    }

    /// <summary>0–100 score shown on the dashboard.</summary>
    public static int Score(IEnumerable<Recommendation> recs)
    {
        var score = 100;
        foreach (var r in recs)
        {
            score -= r.Severity switch
            {
                Severity.Critical => 20,
                Severity.Warning => 8,
                Severity.Info => 2,
                _ => 0,
            };
        }
        return Math.Clamp(score, 0, 100);
    }
}
