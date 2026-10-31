namespace WindowsTuner.Core.Startup;

/// <summary>
/// Task Manager's Startup tab stores an on/off flag per entry under Explorer\StartupApproved as a 12-byte
/// REG_BINARY: the first byte is 0x02 (enabled) or 0x03 (disabled) and the last 8 bytes hold the FILETIME of
/// when it was disabled. Odd first bytes mean disabled; a missing value means enabled.
/// </summary>
public static class StartupApproved
{
    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] Create(bool enabled, DateTime? whenUtc = null)
    {
        var data = new byte[12];
        data[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            var ft = (whenUtc ?? DateTime.UtcNow).ToFileTimeUtc();
            BitConverter.GetBytes(ft).CopyTo(data, 4);
        }
        return data;
    }

    /// <summary>Extracts the executable from a Run command line such as "\"C:\Program Files\App\app.exe\" --tray".</summary>
    public static string ExtractExecutable(string command)
    {
        var c = Environment.ExpandEnvironmentVariables(command.Trim());
        if (c.Length == 0) return "";
        if (c[0] == '"')
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : c.Trim('"');
        }

        // Unquoted: take the longest prefix ending in .exe (paths with spaces are often left unquoted).
        var idx = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) return c[..(idx + 4)];
        var space = c.IndexOf(' ');
        return space > 0 ? c[..space] : c;
    }
}
