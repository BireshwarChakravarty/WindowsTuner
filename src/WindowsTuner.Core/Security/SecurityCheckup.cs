using System.Text.Json;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Security;

public enum CheckStatus { Good, Warning, Bad, Unknown }

public sealed class SecurityCheck : ObservableObject
{
    private CheckStatus _status;
    private string _detail = "";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public CheckStatus Status { get => _status; set { if (Set(ref _status, value)) OnPropertyChanged(nameof(Icon)); } }
    public string Detail { get => _detail; set => Set(ref _detail, value); }
    public string? FixLabel { get; init; }
    public bool CanFix => FixLabel is not null && Status is CheckStatus.Warning or CheckStatus.Bad;
    public string Icon => Status switch { CheckStatus.Good => "✔", CheckStatus.Warning => "⚠", CheckStatus.Bad => "⛔", _ => "?" };
}

/// <summary>A one-page security review: the settings that matter most on a home PC.</summary>
public static class SecurityCheckup
{
    public const string Script = """
        $ErrorActionPreference = 'SilentlyContinue'
        $o = [ordered]@{}
        $fw = @(Get-NetFirewallProfile)
        $o.FirewallOff = @($fw | Where-Object { -not $_.Enabled } | ForEach-Object { $_.Name }) -join ','
        $o.FirewallKnown = $fw.Count -gt 0
        $o.Uac = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System').EnableLUA
        $o.Smb1 = (Get-SmbServerConfiguration).EnableSMB1Protocol
        try { $o.SecureBoot = Confirm-SecureBootUEFI } catch { $o.SecureBoot = $null }
        $tpm = Get-Tpm
        $o.TpmPresent = $tpm.TpmPresent; $o.TpmReady = $tpm.TpmReady
        $bl = Get-BitLockerVolume -MountPoint $env:SystemDrive
        $o.BitLocker = if ($bl) { [string]$bl.ProtectionStatus } else { $null }
        $mp = Get-MpComputerStatus
        $o.RealTime = $mp.RealTimeProtectionEnabled; $o.Tamper = $mp.IsTamperProtected; $o.SigAge = $mp.AntivirusSignatureAge
        $wu = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
        $o.WuPausedUntil = [string]$wu.PauseUpdatesExpiryTime
        $o.Rdp = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server').fDenyTSConnections
        $o.AutoLogon = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon').AutoAdminLogon
        $o.Guest = (Get-LocalUser -Name Guest).Enabled
        $o.SmartScreen = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer').SmartScreenEnabled
        $o.Hvci = (Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard).SecurityServicesRunning -contains 2
        $o | ConvertTo-Json -Compress
        """;

    public static IReadOnlyList<SecurityCheck> Evaluate(string json)
    {
        var start = json.IndexOf('{');
        using var doc = JsonDocument.Parse(start >= 0 ? json[start..] : "{}");
        var r = doc.RootElement;
        string? S(string n) => r.TryGetProperty(n, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => v.GetString(),
            _ => v.ToString(),
        } : null;
        bool? B(string n) => S(n)?.ToLowerInvariant() switch { "true" or "1" => true, "false" or "0" => false, _ => null };

        var list = new List<SecurityCheck>();
        void Add(string id, string name, CheckStatus st, string detail, string? fix = null) =>
            list.Add(new SecurityCheck { Id = id, Name = name, Status = st, Detail = detail, FixLabel = fix });

        var fwOff = S("FirewallOff") ?? "";
        if (B("FirewallKnown") == true)
            Add("firewall", "Windows Firewall", fwOff.Length == 0 ? CheckStatus.Good : CheckStatus.Bad,
                fwOff.Length == 0 ? "On for all network types." : $"Off for: {fwOff}.", "Turn on");
        else Add("firewall", "Windows Firewall", CheckStatus.Unknown, "Managed by another security product or unavailable.");

        var rt = B("RealTime");
        Add("realtime", "Real-time virus protection", rt switch { true => CheckStatus.Good, false => CheckStatus.Bad, _ => CheckStatus.Unknown },
            rt switch { true => "Microsoft Defender is watching files as they open.", false => "Off — turn it on in Windows Security (unless another antivirus is installed).", _ => "Another antivirus is probably in charge." });

        var age = int.TryParse(S("SigAge"), out var a) ? a : -1;
        if (age >= 0)
            Add("signatures", "Virus definitions", age <= 2 ? CheckStatus.Good : age <= 7 ? CheckStatus.Warning : CheckStatus.Bad,
                age <= 0 ? "Up to date." : $"{age} day(s) old.", "Update now");

        if (B("Tamper") is { } tamper)
            Add("tamper", "Tamper protection", tamper ? CheckStatus.Good : CheckStatus.Warning,
                tamper ? "Malware can't switch Defender off." : "Off — turn on in Windows Security → Virus & threat protection settings.");

        var uac = S("Uac");
        Add("uac", "User Account Control", uac == "0" ? CheckStatus.Bad : CheckStatus.Good,
            uac == "0" ? "Disabled — every program runs with full rights." : "On.", "Turn on (restart)");

        if (B("Smb1") is { } smb1)
            Add("smb1", "SMBv1 file sharing", smb1 ? CheckStatus.Bad : CheckStatus.Good,
                smb1 ? "The obsolete protocol abused by WannaCry is enabled." : "Disabled.", "Disable");

        var paused = S("WuPausedUntil");
        var pausedActive = DateTime.TryParse(paused, out var until) && until > DateTime.Now;
        Add("wupause", "Windows Update", pausedActive ? CheckStatus.Warning : CheckStatus.Good,
            pausedActive ? $"Paused until {until:d}." : "Not paused.", "Resume updates");

        if (B("SecureBoot") is { } sb)
            Add("secureboot", "Secure Boot", sb ? CheckStatus.Good : CheckStatus.Warning,
                sb ? "On — blocks boot-level malware." : "Off — enable it in your BIOS/UEFI settings.");

        if (B("TpmPresent") is { } tpm)
            Add("tpm", "TPM security chip", tpm && B("TpmReady") == true ? CheckStatus.Good : CheckStatus.Warning,
                tpm ? (B("TpmReady") == true ? "Present and ready." : "Present but not ready — check BIOS settings.") : "Not found or disabled in BIOS (required for Windows 11).");

        var bl = S("BitLocker");
        if (bl is not null)
            Add("bitlocker", "Drive encryption (BitLocker / Device Encryption)", bl is "On" or "1" ? CheckStatus.Good : CheckStatus.Warning,
                bl is "On" or "1" ? "System drive is encrypted." : "Not encrypted — a stolen laptop exposes all your files. Make sure you back up the recovery key if you turn it on.");

        if (S("Rdp") == "0")
            Add("rdp", "Remote Desktop", CheckStatus.Warning, "Enabled — turn off if you don't use it.", "Turn off");

        if (S("AutoLogon") == "1")
            Add("autologon", "Automatic sign-in", CheckStatus.Warning, "Windows signs in without a password; anyone can use the PC.");

        if (B("Guest") == true)
            Add("guest", "Guest account", CheckStatus.Warning, "Enabled.", "Disable");

        if (S("SmartScreen") is "Off")
            Add("smartscreen", "SmartScreen", CheckStatus.Warning, "Off — Windows won't warn about unrecognised downloads.");

        if (B("Hvci") is { } hvci)
            Add("hvci", "Memory integrity (Core isolation)", hvci ? CheckStatus.Good : CheckStatus.Warning,
                hvci ? "On." : "Off — turn on in Windows Security → Device security → Core isolation (needs compatible drivers).");

        return list.OrderByDescending(c => c.Status == CheckStatus.Bad).ThenByDescending(c => c.Status == CheckStatus.Warning).ToList();
    }

    public static async Task<IReadOnlyList<SecurityCheck>> RunAsync(CancellationToken ct = default)
    {
        var r = await PowerShell.RunAsync(Script, ct: ct);
        try { return Evaluate(r.StdOut); }
        catch (Exception ex)
        {
            Log.Warn("Security checkup failed: " + ex.Message);
            return [];
        }
    }

    public static async Task<bool> FixAsync(SecurityCheck check, CancellationToken ct = default)
    {
        var script = check.Id switch
        {
            "firewall" => "Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled True",
            "signatures" => "Update-MpSignature",
            "uac" => "Set-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System' -Name EnableLUA -Value 1 -Type DWord",
            "smb1" => "Set-SmbServerConfiguration -EnableSMB1Protocol $false -Force",
            "wupause" => "$k='HKLM:\\SOFTWARE\\Microsoft\\WindowsUpdate\\UX\\Settings'; 'PauseUpdatesExpiryTime','PauseFeatureUpdatesEndTime','PauseQualityUpdatesEndTime','PauseUpdatesStartTime','PauseFeatureUpdatesStartTime','PauseQualityUpdatesStartTime' | ForEach-Object { Remove-ItemProperty -Path $k -Name $_ -ErrorAction SilentlyContinue }",
            "rdp" => "Set-ItemProperty 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Terminal Server' -Name fDenyTSConnections -Value 1 -Type DWord",
            "guest" => "Disable-LocalUser -Name Guest",
            _ => null,
        };
        if (script is null) return false;
        var r = await PowerShell.RunAsync("$ErrorActionPreference='Stop'; " + script, Log.Output, ct);
        if (r.Success)
        {
            check.Status = CheckStatus.Good;
            check.Detail = "Fixed" + (check.Id == "uac" ? " — restart to apply." : ".");
            Log.Success($"{check.Name}: fixed.");
        }
        else Log.Error($"{check.Name}: {r.StdErr.Trim()}");
        return r.Success;
    }
}
