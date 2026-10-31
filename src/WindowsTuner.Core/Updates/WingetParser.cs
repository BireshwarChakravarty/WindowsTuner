using System.Text.Json;

namespace WindowsTuner.Core.Updates;

/// <summary>
/// Parses `winget upgrade` table output. Column headers are localized and wide (CJK) characters shift
/// fixed positions, so rows are parsed right-to-left by whitespace tokens: the Id, Version, Available and
/// Source columns never contain spaces, while the Name column can.
/// </summary>
public static class WingetParser
{
    public static IReadOnlyList<AppUpdate> ParseUpgradeTable(string output)
    {
        var results = new List<AppUpdate>();
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(Infrastructure.ProcessRunner.CleanConsoleLine)
            .ToList();

        var columnCount = 0;
        var inTable = false;
        var explicitSection = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (IsSeparator(line))
            {
                // Header is the previous non-empty line.
                var header = i > 0 ? lines[i - 1] : "";
                columnCount = header.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                if (columnCount < 4) columnCount = 5;
                if (columnCount > 5) columnCount = 5;
                inTable = true;
                continue;
            }

            if (line.TrimEnd().EndsWith(':') &&
                (line.Contains("explicit", StringComparison.OrdinalIgnoreCase) || line.Contains("pinned", StringComparison.OrdinalIgnoreCase)))
            {
                explicitSection = true;
                inTable = false;
                continue;
            }

            if (!inTable || string.IsNullOrWhiteSpace(line)) continue;

            // Footer lines such as "3 upgrades available." end the table; a following header restarts it.
            if (IsFooter(line))
            {
                inTable = false;
                continue;
            }

            var row = ParseRow(line, columnCount, explicitSection);
            if (row is not null) results.Add(row);
        }

        // A package can appear twice (e.g. two installed versions); keep the first.
        return results.GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    private static bool IsSeparator(string line) =>
        line.Length >= 10 && line.All(c => c == '-' || c == '─');

    private static bool IsFooter(string line)
    {
        var t = line.TrimStart();
        if (t.Length > 0 && char.IsDigit(t[0]) && (t.Contains("upgrade", StringComparison.OrdinalIgnoreCase)
                                                  || t.Contains("package", StringComparison.OrdinalIgnoreCase)))
            return true;
        return t.StartsWith("The following", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith("No installed package", StringComparison.OrdinalIgnoreCase)
               || t.Contains("--include-unknown", StringComparison.OrdinalIgnoreCase)
               || t.Contains("--include-pinned", StringComparison.OrdinalIgnoreCase);
    }

    internal static AppUpdate? ParseRow(string line, int columnCount, bool explicitSection)
    {
        var tokens = MergeComparisonTokens(line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList());
        var fixedCols = columnCount - 1; // everything except Name
        if (tokens.Count < fixedCols + 1) return null;

        var tail = tokens.Skip(tokens.Count - fixedCols).ToList();
        var name = string.Join(' ', tokens.Take(tokens.Count - fixedCols));

        string id = tail[0], installed = tail[1], available = tail[2];
        var source = fixedCols >= 4 ? tail[3] : "";

        // Sanity checks: an Id never looks like a bare version, and a version must contain a digit or "Unknown".
        if (!LooksLikeVersion(installed) || !LooksLikeVersion(available)) return null;
        if (string.IsNullOrWhiteSpace(name)) name = id;

        return new AppUpdate
        {
            Name = name,
            Id = id,
            InstalledVersion = installed,
            AvailableVersion = available,
            Source = source,
            RequiresExplicitTarget = explicitSection,
        };
    }

    private static List<string> MergeComparisonTokens(List<string> tokens)
    {
        // winget prints version ranges like "< 1.2.3" — glue the operator to the version.
        var merged = new List<string>(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is "<" or ">" && i + 1 < tokens.Count)
            {
                merged.Add(tokens[i] + " " + tokens[i + 1]);
                i++;
            }
            else merged.Add(tokens[i]);
        }
        return merged;
    }

    private static bool LooksLikeVersion(string value) =>
        value.Any(char.IsDigit) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || value.EndsWith('…');

    /// <summary>Parses the JSON produced by the Microsoft.WinGet.Client PowerShell module script.</summary>
    public static IReadOnlyList<AppUpdate> ParseModuleJson(string json)
    {
        var list = new List<AppUpdate>();
        json = json.Trim();
        if (json.Length == 0) return list;
        using var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array
            ? doc.RootElement.EnumerateArray().ToList()
            : [doc.RootElement];
        foreach (var e in items)
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            var id = Str(e, "Id");
            if (string.IsNullOrEmpty(id)) continue;
            list.Add(new AppUpdate
            {
                Name = Str(e, "Name") is { Length: > 0 } n ? n : id,
                Id = id,
                InstalledVersion = Str(e, "Version") is { Length: > 0 } v ? v : "Unknown",
                AvailableVersion = Str(e, "Available"),
                Source = Str(e, "Source"),
            });
        }
        return list;
    }

    private static string Str(JsonElement e, string name)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (!p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            return p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => p.Value.ToString(),
            };
        }
        return "";
    }

    /// <summary>Human readable explanation for well-known winget exit codes.</summary>
    public static string DescribeExitCode(int code) => unchecked((uint)code) switch
    {
        0 => "Success",
        0x8A15002B => "No applicable upgrade found (already up to date or not applicable to this system)",
        0x8A150101 => "The application is currently running — close it and retry",
        0x8A150102 => "Another installation is already in progress — retry in a few minutes",
        0x8A150109 => "Restart your PC to finish the installation",
        0x8A150105 => "Not enough disk space",
        0x8A150107 => "No network connection",
        0x8A15010A => "A restart is required before this update can install",
        0x8A15010D => "Already installed",
        0x8A150103 => "One or more files are in use — close open apps and retry",
        _ => $"winget exit code {Infrastructure.Format.HResult(code)}",
    };
}
