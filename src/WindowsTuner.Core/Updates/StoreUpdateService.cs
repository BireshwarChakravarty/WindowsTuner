using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Updates;

/// <summary>Microsoft Store app updates.</summary>
public static class StoreUpdateService
{
    /// <summary>
    /// Asks the Store to scan and update all Store apps (the same as "Get updates" in the Store Library).
    /// Uses the MDM WMI bridge; the call returns immediately and updates continue in the background.
    /// </summary>
    public static async Task<bool> TriggerStoreUpdatesAsync(CancellationToken ct = default)
    {
        Log.Info("Asking Microsoft Store to update all Store apps…");
        var r = await PowerShell.RunAsync("""
            $ErrorActionPreference = 'Stop'
            $app = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_EnterpriseModernAppManagement_AppManagement01'
            $res = $app | Invoke-CimMethod -MethodName UpdateScanMethod
            Write-Output ("ReturnValue=" + $res.ReturnValue)
            """, Log.Output, ct);
        if (r.Success)
        {
            Log.Success("Store update scan started. Store apps will update in the background (open the Store → Library to watch).");
            return true;
        }
        Log.Warn("Could not trigger Store updates automatically. Open Microsoft Store → Library → Get updates. " + r.StdErr.Trim());
        return false;
    }

    public static void OpenStoreLibrary() => Shell.Open("ms-windows-store://downloadsandupdates");
}
