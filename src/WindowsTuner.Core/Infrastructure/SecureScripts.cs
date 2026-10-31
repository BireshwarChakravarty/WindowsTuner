using System.Security.AccessControl;
using System.Security.Principal;

namespace WindowsTuner.Core.Infrastructure;

/// <summary>
/// Folder for the short-lived helper scripts WindowsTuner runs (PowerShell / SYSTEM tasks):
/// C:\ProgramData\WindowsTuner\Scripts, readable and writable only by Administrators and SYSTEM, so a
/// non-elevated program can't swap a script between it being written and it being run.
/// Scripts are plain, readable files — never encoded or obfuscated.
/// </summary>
public static class SecureScripts
{
    private static string? _dir;

    public static string Directory => _dir ??= Create();

    private static string Create()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WindowsTuner", "Scripts");
        try
        {
            if (OperatingSystem.IsWindows() && Elevation.IsAdministrator)
            {
                var security = new DirectorySecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
                {
                    security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                }
                var info = new DirectoryInfo(dir);
                if (!info.Exists) info.Create(security);
                else info.SetAccessControl(security);
                return dir;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Could not secure the scripts folder, using your profile instead: " + ex.Message);
        }
        var fallback = Path.Combine(AppPaths.Root, "Scripts");
        System.IO.Directory.CreateDirectory(fallback);
        return fallback;
    }

    public static string NewPath(string extension) =>
        Path.Combine(Directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}{extension}");

    public static void TryDelete(params string[] files)
    {
        foreach (var f in files)
        {
            try { File.Delete(f); } catch { }
        }
    }
}
