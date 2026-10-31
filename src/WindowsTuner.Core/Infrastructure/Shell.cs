using System.Diagnostics;

namespace WindowsTuner.Core.Infrastructure;

public static class Shell
{
    /// <summary>Opens a URL, ms-settings: URI, file or folder with the default handler.</summary>
    public static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not open {target}: {ex.Message}");
        }
    }

    public static void Start(string file, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Could not start {file}: {ex.Message}");
        }
    }
}
