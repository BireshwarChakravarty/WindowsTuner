using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Safety;

/// <summary>System Restore points — the safety net taken before any batch of changes.</summary>
public static class RestorePointService
{
    public static async Task<bool> CreateAsync(string description, CancellationToken ct = default)
    {
        Log.Info("Creating a System Restore point…");
        // Windows allows only one restore point per 24h by default; lift that limit for this call, make sure
        // System Protection is on for the system drive, then create the point.
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $key = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
            $old = (Get-ItemProperty -Path $key -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue).SystemRestorePointCreationFrequency
            Set-ItemProperty -Path $key -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord
            try {
                Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
                Checkpoint-Computer -Description {{PowerShell.Quote(description)}} -RestorePointType 'MODIFY_SETTINGS'
                Write-Output 'Restore point created.'
            } finally {
                if ($null -eq $old) { Remove-ItemProperty -Path $key -Name SystemRestorePointCreationFrequency -ErrorAction SilentlyContinue }
                else { Set-ItemProperty -Path $key -Name SystemRestorePointCreationFrequency -Value $old -Type DWord }
            }
            """;
        var r = await PowerShell.RunAsync(script, Log.Output, ct);
        if (r.Success)
        {
            Log.Success($"Restore point \"{description}\" created.");
            return true;
        }
        Log.Warn("Could not create a restore point (System Protection may be disabled by policy): " + r.StdErr.Trim());
        return false;
    }

    public static void OpenSystemRestore() => Shell.Start("rstrui.exe", "");
    public static void OpenSystemProtection() => Shell.Start("SystemPropertiesProtection.exe", "");
}
