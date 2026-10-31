using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Network;

namespace WindowsTuner.App.Views;

public partial class NetworkView : UserControl, IPage
{
    public NetworkView() => InitializeComponent();

    public async Task OnNavigatedToAsync()
    {
        AdapterGrid.ItemsSource = NetworkService.GetAdapters();
        var ping = await NetworkService.PingAsync("1.1.1.1");
        PingText.Text = ping is { } ms ? $"Internet reachable · ping {ms:0} ms" : "⚠ Can't reach the internet (ping to 1.1.1.1 failed — may be blocked by your network)";
    }

    private void Bench_Click(object sender, RoutedEventArgs e) => _ = Ui.RunAsync("Testing DNS providers", async ct =>
    {
        DnsGrid.ItemsSource = await DnsBenchmark.RunAsync(ct);
    }, BenchButton);

    private void UseDns_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DnsResult r }) return;
        if (!Ui.Confirm($"Use {r.Name} DNS ({r.Servers}) on all connected adapters?\n\nYou can switch back with \"Use automatic DNS\".")) return;
        _ = Ui.RunAsync($"Switching DNS to {r.Name}", async ct =>
        {
            await NetworkService.SetDnsAsync(r.Provider, ct);
            AdapterGrid.ItemsSource = NetworkService.GetAdapters();
        });
    }

    private void ResetDns_Click(object sender, RoutedEventArgs e) => _ = Ui.RunAsync("Resetting DNS", async ct =>
    {
        await NetworkService.ResetDnsAsync(ct);
        AdapterGrid.ItemsSource = NetworkService.GetAdapters();
    }, ResetDnsButton);

    private void Flush_Click(object sender, RoutedEventArgs e) =>
        _ = Ui.RunAsync("Flushing DNS", async ct => { if ((await NetworkService.FlushDnsAsync(ct)).Success) Log.Success("DNS cache flushed."); }, FlushButton);

    private void ResetStack_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.Confirm("Reset Winsock and TCP/IP?\n\nCustom network settings (static IPs, VPN adapters' bindings) may need to be set up again. A restart is required.")) return;
        _ = Ui.RunAsync("Resetting network", async ct =>
        {
            if (await NetworkService.ResetNetworkStackAsync(ct)) Ui.OfferRestart("The network reset finishes after a restart.");
        }, ResetStackButton);
    }

    private void NetSettings_Click(object sender, RoutedEventArgs e) => Shell.Open("ms-settings:network-status");
    private void Troubleshoot_Click(object sender, RoutedEventArgs e) => Shell.Open("ms-settings:troubleshoot");
    private void DataUsage_Click(object sender, RoutedEventArgs e) => Shell.Open("ms-settings:datausage");
}
