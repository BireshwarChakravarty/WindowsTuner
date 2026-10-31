using System.Text.RegularExpressions;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Performance;

public sealed record PowerPlan(Guid Guid, string Name, bool IsActive);

public static partial class PowerPlanService
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\((.*)\)\s*(\*)?\s*$")]
    private static partial Regex PlanLine();

    /// <summary>Parses `powercfg /list`. Works for every Windows display language because it keys off the GUID.</summary>
    public static IReadOnlyList<PowerPlan> ParseList(string output)
    {
        var plans = new List<PowerPlan>();
        foreach (var raw in output.Split('\n'))
        {
            var m = PlanLine().Match(raw.TrimEnd('\r'));
            if (!m.Success) continue;
            plans.Add(new PowerPlan(Guid.Parse(m.Groups[1].Value), m.Groups[2].Value.Trim(), m.Groups[3].Success));
        }
        return plans;
    }

    public static async Task<IReadOnlyList<PowerPlan>> GetPlansAsync()
    {
        var r = await ProcessRunner.RunAsync("powercfg.exe", "/list");
        return ParseList(r.StdOut);
    }

    public static async Task<bool> SetActiveAsync(Guid plan)
    {
        var r = await ProcessRunner.RunAsync("powercfg.exe", $"/setactive {plan}");
        if (r.Success) Log.Success($"Power plan activated: {plan}");
        else Log.Error("Could not change power plan: " + r.Combined.Trim());
        return r.Success;
    }

    /// <summary>Adds the hidden "Ultimate Performance" plan (desktops; not available on Modern Standby laptops).</summary>
    public static async Task<Guid?> AddUltimatePerformanceAsync()
    {
        var r = await ProcessRunner.RunAsync("powercfg.exe", $"-duplicatescheme {UltimatePerformance}");
        var plan = ParseList(r.StdOut + "\n").FirstOrDefault()
                   ?? (Regex.Match(r.StdOut, @"[0-9a-fA-F-]{36}") is { Success: true } m ? new PowerPlan(Guid.Parse(m.Value), "Ultimate Performance", false) : null);
        if (plan is null)
        {
            Log.Warn("Ultimate Performance isn't available on this PC (common on laptops with Modern Standby). Use High performance or Balanced.");
            return null;
        }
        Log.Success("Added the Ultimate Performance power plan.");
        return plan.Guid;
    }

    public static void OpenPowerOptions() => Shell.Start("control.exe", "powercfg.cpl");
}
