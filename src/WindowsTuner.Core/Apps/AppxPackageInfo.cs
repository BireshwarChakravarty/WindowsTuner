using System.Text.Json;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Apps;

public enum BloatRating { Keep, Optional, Suggested }

public sealed class AppxPackageInfo : ObservableObject
{
    private bool _isSelected;
    private string _status = "";

    public required string Name { get; init; }
    public required string PackageFullName { get; init; }
    public string Version { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string FriendlyName => BloatCatalog.FriendlyName(Name);
    public BloatRating Rating => BloatCatalog.Rate(Name);
    public bool IsProtected => BloatCatalog.IsProtected(Name);
    public string RatingText => IsProtected ? "System — keep" : Rating switch
    {
        BloatRating.Suggested => "Safe to remove",
        BloatRating.Optional => "Optional",
        _ => "",
    };

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value && !IsProtected); }
    public string Status { get => _status; set => Set(ref _status, value); }

    public static IReadOnlyList<AppxPackageInfo> ParseJson(string json)
    {
        var list = new List<AppxPackageInfo>();
        json = json.Trim();
        var start = json.IndexOfAny(['[', '{']);
        if (start < 0) return list;
        using var doc = JsonDocument.Parse(json[start..]);
        var items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.EnumerateArray().ToList() : [doc.RootElement];
        foreach (var e in items)
        {
            var name = e.TryGetProperty("Name", out var n) ? n.GetString() : null;
            var full = e.TryGetProperty("PackageFullName", out var f) ? f.GetString() : null;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(full)) continue;
            list.Add(new AppxPackageInfo
            {
                Name = name,
                PackageFullName = full,
                Version = e.TryGetProperty("Version", out var v) ? v.ToString() : "",
                Publisher = e.TryGetProperty("Publisher", out var p) ? p.GetString() ?? "" : "",
            });
        }
        return list;
    }
}

/// <summary>
/// Opinionated ratings for preinstalled Store apps. Everything rated "Suggested" can be reinstalled from the
/// Microsoft Store at any time; "Protected" packages are never offered for removal.
/// </summary>
public static class BloatCatalog
{
    private static readonly Dictionary<string, string> Suggested = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.BingNews"] = "Microsoft News",
        ["Microsoft.BingWeather"] = "Weather",
        ["Microsoft.BingFinance"] = "Money",
        ["Microsoft.BingSports"] = "Sports",
        ["Microsoft.BingSearch"] = "Bing Search",
        ["Microsoft.GetHelp"] = "Get Help",
        ["Microsoft.Getstarted"] = "Tips",
        ["Microsoft.MicrosoftSolitaireCollection"] = "Solitaire Collection",
        ["Microsoft.MicrosoftOfficeHub"] = "Microsoft 365 (Office) hub",
        ["Microsoft.People"] = "People",
        ["Microsoft.PowerAutomateDesktop"] = "Power Automate",
        ["Microsoft.Todos"] = "Microsoft To Do",
        ["Microsoft.WindowsFeedbackHub"] = "Feedback Hub",
        ["Microsoft.WindowsMaps"] = "Maps",
        ["Microsoft.ZuneVideo"] = "Films & TV",
        ["Microsoft.MixedReality.Portal"] = "Mixed Reality Portal",
        ["Microsoft.SkypeApp"] = "Skype",
        ["Microsoft.549981C3F5F10"] = "Cortana",
        ["Microsoft.Messaging"] = "Messaging",
        ["Microsoft.OneConnect"] = "Mobile Plans",
        ["Microsoft.Print3D"] = "Print 3D",
        ["Microsoft.Microsoft3DViewer"] = "3D Viewer",
        ["Microsoft.WindowsCommunicationsApps"] = "Mail and Calendar (retired)",
        ["Microsoft.MicrosoftJournal"] = "Journal",
        ["Clipchamp.Clipchamp"] = "Clipchamp video editor",
        ["MicrosoftTeams"] = "Teams (personal, classic)",
        ["MSTeams"] = "Microsoft Teams (personal)",
        ["Microsoft.Windows.DevHome"] = "Dev Home",
        ["Microsoft.Copilot"] = "Copilot",
        ["Microsoft.Windows.Ai.Copilot.Provider"] = "Copilot provider",
        ["Microsoft.OutlookForWindows"] = "Outlook (new)",
        ["Microsoft.Wallet"] = "Wallet",
    };

    private static readonly Dictionary<string, string> Optional = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft.YourPhone"] = "Phone Link",
        ["Microsoft.XboxApp"] = "Xbox Console Companion",
        ["Microsoft.GamingApp"] = "Xbox",
        ["Microsoft.XboxGamingOverlay"] = "Xbox Game Bar",
        ["Microsoft.Xbox.TCUI"] = "Xbox TCUI",
        ["Microsoft.XboxIdentityProvider"] = "Xbox Identity Provider",
        ["Microsoft.XboxSpeechToTextOverlay"] = "Xbox Speech to Text",
        ["Microsoft.ZuneMusic"] = "Media Player",
        ["Microsoft.WindowsSoundRecorder"] = "Sound Recorder",
        ["Microsoft.WindowsAlarms"] = "Clock",
        ["Microsoft.WindowsCamera"] = "Camera",
        ["Microsoft.MicrosoftStickyNotes"] = "Sticky Notes",
        ["Microsoft.Office.OneNote"] = "OneNote for Windows 10",
        ["Microsoft.ScreenSketch"] = "Snipping Tool",
        ["Microsoft.Paint"] = "Paint",
        ["Microsoft.Windows.Photos"] = "Photos",
        ["MicrosoftCorporationII.QuickAssist"] = "Quick Assist",
        ["Microsoft.WindowsCalculator"] = "Calculator",
        ["Microsoft.WindowsNotepad"] = "Notepad",
        ["Microsoft.WindowsTerminal"] = "Terminal",
    };

    /// <summary>Removing these breaks updates, security or the shell.</summary>
    private static readonly string[] ProtectedPrefixes =
    [
        "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller", "Microsoft.SecHealthUI",
        "Microsoft.VCLibs", "Microsoft.NET", "Microsoft.UI.Xaml", "Microsoft.WindowsAppRuntime", "Microsoft.Services.Store",
        "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.StartMenuExperienceHost", "Microsoft.AAD.BrokerPlugin",
        "Microsoft.AccountsControl", "Microsoft.Windows.CloudExperienceHost", "Microsoft.LockApp", "Microsoft.Win32WebViewHost",
        "Microsoft.WebExperience", "Microsoft.HEIFImageExtension", "Microsoft.HEVCVideoExtension", "Microsoft.VP9VideoExtensions",
        "Microsoft.WebMediaExtensions", "Microsoft.WebpImageExtension", "Microsoft.RawImageExtension", "Microsoft.AV1VideoExtension",
        "Microsoft.Windows.SecureAssessmentBrowser", "Microsoft.MicrosoftEdge", "Microsoft.Windows.Search", "MicrosoftWindows.Client",
        "Microsoft.ApplicationCompatibilityEnhancements", "windows.immersivecontrolpanel", "Microsoft.Windows.Apprep",
        "Microsoft.WindowsAppRuntime", "Microsoft.Winget", "NVIDIACorp", "AdvancedMicroDevicesInc", "AppUp.Intel", "RealtekSemiconductor",
        "DolbyLaboratories", "WavesAudio", "SynapticsIncorporated", "ELANMicroelectronics",
    ];

    // Promotional third-party apps preinstalled by Windows or the PC maker.
    private static readonly string[] SuggestedPrefixes =
    [
        "king.com.", "SpotifyAB.", "Disney.", "Facebook.", "BytedancePte.", "AmazonVideo.", "5A894077.McAfeeSecurity",
        "Clipchamp.", "AdobeSystemsIncorporated.AdobeExpress", "Duolingo", "PricelinePartnerNetwork.", "4DF9E0F8.Netflix",
        "C27EB4BA.Dropbox", "E046963F.LenovoCompanion", "Playtika.", "ROBLOXCORPORATION.", "TikTok", "Instagram",
    ];

    public static bool IsProtected(string name) =>
        ProtectedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    public static BloatRating Rate(string name)
    {
        if (IsProtected(name)) return BloatRating.Keep;
        if (Suggested.ContainsKey(name) || SuggestedPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return BloatRating.Suggested;
        return Optional.ContainsKey(name) ? BloatRating.Optional : BloatRating.Keep;
    }

    public static string FriendlyName(string name) =>
        Suggested.TryGetValue(name, out var s) ? s : Optional.TryGetValue(name, out var o) ? o : name;
}

public static class AppxService
{
    public static async Task<IReadOnlyList<AppxPackageInfo>> GetRemovablePackagesAsync(CancellationToken ct = default)
    {
        const string script = """
            $pk = @(Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } |
                Select-Object Name, PackageFullName, @{n='Version';e={[string]$_.Version}}, Publisher)
            ConvertTo-Json -InputObject $pk -Compress
            """;
        var r = await PowerShell.RunAsync(script, ct: ct);
        var list = AppxPackageInfo.ParseJson(r.StdOut);
        return list.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderByDescending(p => p.Rating).ThenBy(p => p.FriendlyName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static async Task<bool> RemoveAsync(AppxPackageInfo pkg, bool alsoForNewUsers, CancellationToken ct = default)
    {
        if (pkg.IsProtected) return false;
        Log.Info($"Removing {pkg.FriendlyName} ({pkg.Name})…");
        var script = $"Remove-AppxPackage -Package {PowerShell.Quote(pkg.PackageFullName)} -ErrorAction Stop";
        if (alsoForNewUsers)
            script += $"; Get-AppxProvisionedPackage -Online | Where-Object DisplayName -eq {PowerShell.Quote(pkg.Name)} | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Out-Null";
        var r = await PowerShell.RunAsync(script, Log.Output, ct);
        pkg.Status = r.Success ? "Removed" : "Failed: " + r.StdErr.Trim().Split('\n').FirstOrDefault();
        if (r.Success) Log.Success($"Removed {pkg.FriendlyName}."); else Log.Error($"{pkg.FriendlyName}: {pkg.Status}");
        return r.Success;
    }
}
