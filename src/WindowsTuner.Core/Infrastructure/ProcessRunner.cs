using System.Diagnostics;
using System.Text;

namespace WindowsTuner.Core.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string Combined => string.IsNullOrWhiteSpace(StdErr) ? StdOut : StdOut + Environment.NewLine + StdErr;
}

public static class ProcessRunner
{
    /// <summary>
    /// Runs a console program hidden, streams cleaned output lines to <paramref name="onLine"/>,
    /// and returns the full output. The process tree is killed if the token is cancelled.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        Action<string>? onLine = null,
        Encoding? encoding = null,
        CancellationToken ct = default,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = encoding ?? Encoding.UTF8,
            StandardErrorEncoding = encoding ?? Encoding.UTF8,
        };
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => Handle(e.Data, stdout);
        process.ErrorDataReceived += (_, e) => Handle(e.Data, stderr);

        void Handle(string? data, StringBuilder sink)
        {
            if (data is null) return;
            lock (sink) sink.AppendLine(data);
            var clean = CleanConsoleLine(data);
            if (clean.Length > 0 && !IsProgressNoise(clean)) onLine?.Invoke(clean);
        }

        try
        {
            if (!process.Start()) return new ProcessResult(-1, "", $"Could not start {fileName}");
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", $"Could not start {fileName}: {ex.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }
        process.WaitForExit(); // flush async readers

        string o, e2;
        lock (stdout) o = stdout.ToString();
        lock (stderr) e2 = stderr.ToString();
        return new ProcessResult(process.ExitCode, o, e2);
    }

    /// <summary>Applies carriage returns and backspaces the way a console would, then trims.</summary>
    public static string CleanConsoleLine(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            switch (ch)
            {
                case '\r':
                    sb.Clear();
                    break;
                case '\b':
                    if (sb.Length > 0) sb.Length--;
                    break;
                case '\0':
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }
        // Text after a CR that is followed only by whitespace means the line was blanked; keep the last visible text.
        return sb.ToString().Trim();
    }

    /// <summary>Spinner characters and progress bars that would flood the log.</summary>
    public static bool IsProgressNoise(string line)
    {
        if (line.Length <= 2 && line.All(c => c is '-' or '\\' or '|' or '/' or ' ')) return true;
        return line.Contains('█') || line.Contains('▒');
    }
}
