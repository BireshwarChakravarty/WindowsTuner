using System.Globalization;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Maintenance;

/// <summary>Registers a weekly Task Scheduler job that runs "WindowsTuner.exe --tune-up" elevated.</summary>
public static class Scheduler
{
    public const string TaskName = @"WindowsTuner\Weekly Tune-up";

    public static string BuildCreateArguments(string exePath, DayOfWeek day, int hour) =>
        $"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --tune-up\" /SC WEEKLY /D {day.ToString()[..3].ToUpperInvariant()} " +
        $"/ST {hour.ToString("00", CultureInfo.InvariantCulture)}:00 /RL HIGHEST /F";

    public static async Task<bool> EnableAsync(string exePath, DayOfWeek day, int hour)
    {
        var r = await ProcessRunner.RunAsync("schtasks.exe", BuildCreateArguments(exePath, day, hour), Log.Output);
        if (r.Success)
        {
            // Catch up on missed runs (PC was off) — schtasks.exe can't set this flag itself.
            await PowerShell.RunAsync(
                "$t = Get-ScheduledTask -TaskPath '\\WindowsTuner\\' -TaskName 'Weekly Tune-up'; $t.Settings.StartWhenAvailable = $true; Set-ScheduledTask -InputObject $t | Out-Null");
            Log.Success($"Weekly tune-up scheduled for {day} at {hour:00}:00.");
        }
        else Log.Error("Could not create scheduled task: " + r.Combined.Trim());
        return r.Success;
    }

    public static async Task<bool> DisableAsync()
    {
        var r = await ProcessRunner.RunAsync("schtasks.exe", $"/Delete /TN \"{TaskName}\" /F", Log.Output);
        if (r.Success) Log.Success("Weekly tune-up removed.");
        return r.Success;
    }

    public static async Task<bool> IsEnabledAsync()
    {
        var r = await ProcessRunner.RunAsync("schtasks.exe", $"/Query /TN \"{TaskName}\"");
        return r.Success;
    }
}
