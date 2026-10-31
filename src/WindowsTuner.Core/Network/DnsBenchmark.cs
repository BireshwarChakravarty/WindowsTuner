using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Network;

public sealed record DnsProvider(string Name, string[] Servers, string Note);

public sealed class DnsResult : ObservableObject
{
    public required DnsProvider Provider { get; init; }
    public double? MedianMs { get; init; }
    public int Failures { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsFastest { get; set; }
    public string Name => Provider.Name;
    public string Servers => string.Join(", ", Provider.Servers.Take(2));
    public string Note => Provider.Note;
    public string Speed => MedianMs is { } ms ? $"{ms:0} ms" : "no reply";
}

/// <summary>Measures how fast each DNS provider answers from this network (raw UDP DNS queries).</summary>
public static class DnsBenchmark
{
    public static readonly DnsProvider[] Providers =
    [
        new("Cloudflare", ["1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"], "Fast and private"),
        new("Google", ["8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"], "Very reliable"),
        new("Quad9", ["9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"], "Blocks known malware domains"),
        new("OpenDNS", ["208.67.222.222", "208.67.220.220", "2620:119:35::35", "2620:119:53::53"], "Cisco; optional filtering"),
        new("Cloudflare Family", ["1.1.1.3", "1.0.0.3", "2606:4700:4700::1113", "2606:4700:4700::1003"], "Blocks malware and adult content"),
    ];

    private static readonly string[] TestNames = ["www.microsoft.com", "www.wikipedia.org", "www.github.com", "www.bbc.co.uk", "www.amazon.com"];

    public static byte[] BuildQuery(ushort id, string name)
    {
        var bytes = new List<byte>
        {
            (byte)(id >> 8), (byte)id,
            0x01, 0x00, // standard query, recursion desired
            0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // 1 question
        };
        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            var l = System.Text.Encoding.ASCII.GetBytes(label);
            if (l.Length is 0 or > 63) throw new ArgumentException("Invalid DNS name", nameof(name));
            bytes.Add((byte)l.Length);
            bytes.AddRange(l);
        }
        bytes.Add(0);
        bytes.AddRange(new byte[] { 0x00, 0x01, 0x00, 0x01 }); // A, IN
        return bytes.ToArray();
    }

    public static bool IsValidResponse(byte[] response, ushort id) =>
        response.Length >= 12 && response[0] == (byte)(id >> 8) && response[1] == (byte)id && (response[2] & 0x80) != 0;

    public static async Task<double?> QueryAsync(IPAddress server, string name, TimeSpan timeout)
    {
        using var udp = new UdpClient(server.AddressFamily);
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var query = BuildQuery(id, name);
        var sw = Stopwatch.StartNew();
        try
        {
            await udp.SendAsync(query, query.Length, new IPEndPoint(server, 53));
            using var cts = new CancellationTokenSource(timeout);
            var result = await udp.ReceiveAsync(cts.Token);
            return IsValidResponse(result.Buffer, id) ? sw.Elapsed.TotalMilliseconds : null;
        }
        catch { return null; }
    }

    public static IReadOnlyList<IPAddress> CurrentDnsServers() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().DnsAddresses)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.ToString().StartsWith("169.254."))
            .Distinct().ToList();

    public static async Task<IReadOnlyList<DnsResult>> RunAsync(CancellationToken ct = default)
    {
        var current = CurrentDnsServers();
        var providers = Providers.ToList();
        if (current.Count > 0)
            providers.Insert(0, new DnsProvider("Your current DNS", current.Select(c => c.ToString()).ToArray(), "Usually your router / ISP"));

        var results = new List<DnsResult>();
        foreach (var p in providers)
        {
            ct.ThrowIfCancellationRequested();
            var server = IPAddress.Parse(p.Servers.First(s => !s.Contains(':')));
            var times = new List<double>();
            var failures = 0;
            foreach (var name in TestNames)
            {
                var t = await QueryAsync(server, name, TimeSpan.FromSeconds(2));
                if (t is { } ms) times.Add(ms); else failures++;
            }
            times.Sort();
            results.Add(new DnsResult
            {
                Provider = p,
                MedianMs = times.Count > 0 ? times[times.Count / 2] : null,
                Failures = failures,
                IsCurrent = p.Name == "Your current DNS",
            });
        }
        var fastest = results.Where(r => r.MedianMs is not null && !r.IsCurrent).MinBy(r => r.MedianMs);
        if (fastest is not null) fastest.IsFastest = true;
        return results;
    }
}

public sealed record AdapterInfo(string Name, string Description, string Type, string Speed, string IPv4, string Dns);

public static class NetworkService
{
    public static IReadOnlyList<AdapterInfo> GetAdapters() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n =>
            {
                var ip = n.GetIPProperties();
                return new AdapterInfo(
                    n.Name, n.Description, n.NetworkInterfaceType.ToString(),
                    n.Speed > 0 ? $"{n.Speed / 1_000_000} Mbps" : "",
                    string.Join(", ", ip.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address)),
                    string.Join(", ", ip.DnsAddresses.Select(a => a.ToString())));
            })
            .ToList();

    /// <summary>Sets DNS servers on every connected physical adapter.</summary>
    public static async Task<bool> SetDnsAsync(DnsProvider provider, CancellationToken ct = default)
    {
        var servers = string.Join(",", provider.Servers.Select(PowerShell.Quote));
        var r = await PowerShell.RunAsync(
            "Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | ForEach-Object { " +
            $"Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses @({servers}); Write-Output \"DNS set on $($_.Name)\" }}; Clear-DnsClientCache",
            Log.Output, ct);
        if (r.Success) Log.Success($"DNS switched to {provider.Name}.");
        else Log.Error("Could not change DNS: " + r.StdErr.Trim());
        return r.Success;
    }

    /// <summary>Back to automatic DNS from your router (DHCP).</summary>
    public static async Task<bool> ResetDnsAsync(CancellationToken ct = default)
    {
        var r = await PowerShell.RunAsync(
            "Get-NetAdapter -Physical | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }; Clear-DnsClientCache",
            Log.Output, ct);
        if (r.Success) Log.Success("DNS reset to automatic.");
        return r.Success;
    }

    public static Task<ProcessResult> FlushDnsAsync(CancellationToken ct = default) =>
        ProcessRunner.RunAsync("ipconfig.exe", "/flushdns", Log.Output, ct: ct);

    /// <summary>Resets Winsock and the TCP/IP stack — fixes most "connected but no internet" problems. Needs a restart.</summary>
    public static async Task<bool> ResetNetworkStackAsync(CancellationToken ct = default)
    {
        var a = await ProcessRunner.RunAsync("netsh.exe", "winsock reset", Log.Output, ct: ct);
        var b = await ProcessRunner.RunAsync("netsh.exe", "int ip reset", Log.Output, ct: ct);
        await FlushDnsAsync(ct);
        return a.Success && b.Success;
    }

    public static async Task<double?> PingAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 2000);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch { return null; }
    }
}
