using System.Text.Json;

namespace WindowsTuner.Core.Health;

public sealed record DiskHealth(
    string Name,
    string MediaType,
    string BusType,
    string Health,
    long SizeBytes,
    int? WearPercent,
    int? TemperatureC,
    long? ReadErrors)
{
    public bool IsHealthy => Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase);
    public bool IsHdd => MediaType.Equals("HDD", StringComparison.OrdinalIgnoreCase);
    public string Size => Infrastructure.Format.Bytes(SizeBytes);
    public string Wear => WearPercent is { } w ? $"{w} %" : "—";
    public string Temperature => TemperatureC is { } t and > 0 ? $"{t} °C" : "—";

    public static IReadOnlyList<DiskHealth> ParseJson(string json)
    {
        var list = new List<DiskHealth>();
        json = json.Trim();
        var start = json.IndexOfAny(['[', '{']);
        if (start < 0) return list;
        using var doc = JsonDocument.Parse(json[start..]);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        foreach (var e in items)
        {
            list.Add(new DiskHealth(
                Get(e, "FriendlyName") ?? "Disk",
                MapMedia(Get(e, "MediaType")),
                MapBus(Get(e, "BusType")),
                MapHealth(Get(e, "HealthStatus")),
                long.TryParse(Get(e, "Size"), out var size) ? size : 0,
                int.TryParse(Get(e, "Wear"), out var wear) ? wear : null,
                int.TryParse(Get(e, "Temperature"), out var temp) ? temp : null,
                long.TryParse(Get(e, "ReadErrorsUncorrected"), out var re) ? re : null));
        }
        return list;
    }

    private static string? Get(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => v.GetString(),
            _ => v.ToString(),
        } : null;

    // Storage module enums may come through as numbers or names depending on the PowerShell version.
    private static string MapMedia(string? v) => v switch
    {
        "3" => "HDD",
        "4" => "SSD",
        "5" => "SCM",
        "0" or null or "" => "Unspecified",
        _ => v,
    };

    private static string MapHealth(string? v) => v switch
    {
        "0" => "Healthy",
        "1" => "Warning",
        "2" => "Unhealthy",
        "5" or null or "" => "Unknown",
        _ => v,
    };

    private static string MapBus(string? v) => v switch
    {
        "7" => "USB",
        "8" => "RAID",
        "11" => "SATA",
        "17" => "NVMe",
        "3" => "ATA",
        "10" => "SAS",
        null or "" => "",
        _ => v,
    };
}
