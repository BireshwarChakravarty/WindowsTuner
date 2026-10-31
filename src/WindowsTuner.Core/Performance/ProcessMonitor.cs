using System.Diagnostics;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Performance;

public sealed class ProcessUsage : ObservableObject
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public double CpuPercent { get; init; }
    public long MemoryBytes { get; init; }
    public string Memory => Format.Bytes(MemoryBytes);
    public string Cpu => CpuPercent.ToString("0.0") + " %";
    public string Description { get; init; } = "";
}

/// <summary>A quick "what is slowing my PC down right now" snapshot.</summary>
public static class ProcessMonitor
{
    public static Task<IReadOnlyList<ProcessUsage>> SnapshotAsync(TimeSpan sample, CancellationToken ct = default) =>
        Task.Run(() => SnapshotCoreAsync(sample, ct), ct);

    private static async Task<IReadOnlyList<ProcessUsage>> SnapshotCoreAsync(TimeSpan sample, CancellationToken ct)
    {
        var first = new Dictionary<int, TimeSpan>();
        foreach (var p in Process.GetProcesses())
        {
            try { first[p.Id] = p.TotalProcessorTime; } catch { /* protected process */ }
            finally { p.Dispose(); }
        }
        var sw = Stopwatch.StartNew();
        await Task.Delay(sample, ct).ConfigureAwait(false);
        var elapsed = sw.Elapsed.TotalMilliseconds * Environment.ProcessorCount;

        // Group per executable name so 30 browser processes show as one row.
        var groups = new Dictionary<string, (double Cpu, long Mem, int Pid, string Desc)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id is 0 or 4) continue; // Idle, System
                double cpu = 0;
                try
                {
                    if (first.TryGetValue(p.Id, out var t0))
                        cpu = (p.TotalProcessorTime - t0).TotalMilliseconds / elapsed * 100;
                }
                catch { }
                long mem = 0;
                try { mem = p.WorkingSet64; } catch { }
                var desc = "";
                try { desc = p.MainModule?.FileVersionInfo.FileDescription ?? ""; } catch { }
                var name = p.ProcessName;
                groups[name] = groups.TryGetValue(name, out var g)
                    ? (g.Cpu + cpu, g.Mem + mem, g.Pid, g.Desc.Length > 0 ? g.Desc : desc)
                    : (cpu, mem, p.Id, desc);
            }
            finally { p.Dispose(); }
        }

        return groups.Select(kv => new ProcessUsage
            {
                Pid = kv.Value.Pid,
                Name = kv.Key,
                CpuPercent = Math.Max(0, kv.Value.Cpu),
                MemoryBytes = kv.Value.Mem,
                Description = kv.Value.Desc,
            })
            .OrderByDescending(p => p.CpuPercent * 50_000_000 + p.MemoryBytes)
            .Take(25)
            .ToList();
    }

    public static int KillByName(string name)
    {
        var killed = 0;
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(entireProcessTree: true); killed++; }
            catch (Exception ex) { Log.Warn($"Could not end {name} ({p.Id}): {ex.Message}"); }
            finally { p.Dispose(); }
        }
        if (killed > 0) Log.Success($"Ended {killed} {name} process(es).");
        return killed;
    }
}
