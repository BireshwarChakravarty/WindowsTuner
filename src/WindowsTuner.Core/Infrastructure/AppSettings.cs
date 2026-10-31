namespace WindowsTuner.Core.Infrastructure;

public enum ThemeChoice { Dark, Light, System }

/// <summary>User preferences, saved to %LOCALAPPDATA%\WindowsTuner\settings.json.</summary>
public sealed class AppSettings
{
    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;
    /// <summary>Accent colour as #RRGGBB.</summary>
    public string AccentColor { get; set; } = "#3B82F6";

    // Updates
    public bool IncludeUnknownVersions { get; set; } = true;
    /// <summary>winget IDs never updated automatically (e.g. apps you keep on an old version on purpose).</summary>
    public List<string> ExcludedAppIds { get; set; } = [];
    public bool IncludeDriverUpdates { get; set; } = true;
    public bool IncludeOptionalUpdates { get; set; }

    // Tune-up steps
    public HashSet<string> TuneUpSteps { get; set; } = new(Maintenance.TuneUpSteps.Defaults, StringComparer.OrdinalIgnoreCase);
    public List<string> CleanupTargets { get; set; } = [];

    // Behaviour
    public bool CreateRestorePointBeforeChanges { get; set; } = true;
    /// <summary>
    /// Allow WindowsTuner to use the SYSTEM account for protected operations ("hyper admin"). Opt-in: creating
    /// SYSTEM scheduled tasks is legitimate here but resembles what malware does, so antivirus may react to it.
    /// </summary>
    public bool UseSystemAccount { get; set; }
    public bool ScheduledTuneUpEnabled { get; set; }
    public DayOfWeek ScheduleDay { get; set; } = DayOfWeek.Sunday;
    public int ScheduleHour { get; set; } = 12;

    public static AppSettings Load()
    {
        var s = JsonFile.Read<AppSettings>(AppPaths.SettingsFile) ?? new AppSettings();
        SystemRunner.Enabled = s.UseSystemAccount;
        return s;
    }

    public void Save()
    {
        try { JsonFile.Write(AppPaths.SettingsFile, this); }
        catch (Exception ex) { Log.Warn("Could not save settings: " + ex.Message); }
    }
}
