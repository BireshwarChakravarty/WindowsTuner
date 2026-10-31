using System.Text;

namespace WindowsTuner.Core.Infrastructure;

/// <summary>Runs Windows PowerShell 5.1 (always present on Windows 10/11) with UTF-8 output.</summary>
public static class PowerShell
{
    public static string Executable =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");

    public static Task<ProcessResult> RunAsync(string script, Action<string>? onLine = null, CancellationToken ct = default)
    {
        var full = "$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;" + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        var exe = File.Exists(Executable) ? Executable : "powershell.exe";
        return ProcessRunner.RunAsync(exe,
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            onLine, Encoding.UTF8, ct);
    }

    /// <summary>Escapes a value for use inside a single-quoted PowerShell string.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
