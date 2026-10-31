using System.Security.Principal;

namespace WindowsTuner.Core.Infrastructure;

public static class Elevation
{
    public static bool IsAdministrator
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
