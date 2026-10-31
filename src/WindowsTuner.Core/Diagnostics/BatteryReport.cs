using System.Xml.Linq;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Diagnostics;

public sealed record BatteryInfo(string Name, long DesignCapacity, long FullChargeCapacity, int? CycleCount)
{
    public double HealthPercent => DesignCapacity > 0 ? Math.Min(100, 100.0 * FullChargeCapacity / DesignCapacity) : 0;
    public string Summary => $"{Name}: {HealthPercent:0}% of original capacity" + (CycleCount is { } c and > 0 ? $", {c} charge cycles" : "");
}

public static class BatteryReport
{
    /// <summary>Parses `powercfg /batteryreport /xml`.</summary>
    public static IReadOnlyList<BatteryInfo> ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants().Where(e => e.Name.LocalName == "Battery")
            .Select(b =>
            {
                string V(string n) => b.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value.Trim() ?? "";
                var name = $"{V("Manufacturer")} {V("Id")}".Trim();
                return new BatteryInfo(
                    name.Length > 0 ? name : "Battery",
                    long.TryParse(V("DesignCapacity"), out var d) ? d : 0,
                    long.TryParse(V("FullChargeCapacity"), out var f) ? f : 0,
                    int.TryParse(V("CycleCount"), out var c) ? c : null);
            })
            .Where(b => b.DesignCapacity > 0)
            .ToList();
    }

    public static async Task<IReadOnlyList<BatteryInfo>> GetAsync(CancellationToken ct = default)
    {
        var file = Path.Combine(Path.GetTempPath(), $"wt-battery-{Guid.NewGuid():N}.xml");
        try
        {
            var r = await ProcessRunner.RunAsync("powercfg.exe", $"/batteryreport /xml /output \"{file}\"", ct: ct);
            return r.Success && File.Exists(file) ? ParseXml(await File.ReadAllTextAsync(file, ct)) : [];
        }
        catch { return []; }
        finally { try { File.Delete(file); } catch { } }
    }
}
