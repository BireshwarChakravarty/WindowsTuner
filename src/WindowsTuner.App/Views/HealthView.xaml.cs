using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Diagnostics;
using WindowsTuner.Core.Drivers;
using WindowsTuner.Core.Health;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Safety;

namespace WindowsTuner.App.Views;

public partial class HealthView : UserControl, IPage
{
    public HealthView() => InitializeComponent();

    public Task OnNavigatedToAsync() => Ui.RunAsync("Reading disk health", async ct =>
    {
        DiskGrid.ItemsSource = await HealthService.GetDiskHealthAsync(ct);
        var d = await HealthService.GetDefenderStatusAsync(ct);
        DefenderText.Text = d is null
            ? "Microsoft Defender status unavailable (another antivirus may be installed)."
            : $"Microsoft Defender: real-time protection {(d.RealTimeOn ? "on" : "OFF")}, definitions {(d.SignatureAgeDays <= 0 ? "up to date" : d.SignatureAgeDays + " day(s) old")}.";

        var events = await EventLogReport.CollectAsync(ct);
        Findings.ItemsSource = events?.Findings().Select(f => new { f.Problem, f.Text }).ToList();

        var batteries = await BatteryReport.GetAsync(ct);
        BatteryText.Text = batteries.Count == 0 ? "" : "🔋 " + string.Join(" · ", batteries.Select(b => b.Summary));
        if (batteries.Any(b => b.HealthPercent < 70)) BatteryText.Foreground = (System.Windows.Media.Brush)FindResource("WarnBrush");

        var hw = await new DriverService().GetHardwareAsync();
        BiosText.Text = hw.BiosVersion.Length > 0
            ? $"BIOS/UEFI {hw.BiosVersion}" + (hw.BiosDate is { } bd ? $" from {bd:yyyy-MM-dd}" : "") + " — firmware updates come from your PC/motherboard maker (see Drivers)."
            : "";
    });

    private void Repair_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.Confirm("Run DISM RestoreHealth followed by SFC?\n\nThis takes 15–45 minutes. You can keep using the PC.")) return;
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync("Repairing Windows", async ct =>
        {
            var dism = await HealthService.RunDismRestoreHealthAsync(ct);
            if (dism.Success) Log.Success("DISM finished: component store is healthy.");
            else Log.Warn($"DISM exited with {dism.ExitCode} — continuing with SFC.");
            var sfc = await HealthService.RunSfcAsync(ct);
            if (sfc.Success) Log.Success("SFC finished. If it repaired files, restart your PC.");
            else Log.Warn($"SFC exited with {sfc.ExitCode}. Details: C:\\Windows\\Logs\\CBS\\CBS.log");
        }, RepairButton);
    }

    private void Defender_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync("Updating Defender and scanning", async ct =>
        {
            await HealthService.UpdateDefenderSignaturesAsync(ct);
            var r = await HealthService.RunDefenderQuickScanAsync(ct);
            if (r.Success) Log.Success("Quick scan finished. Check Windows Security → Protection history for any findings.");
        }, DefenderButton);
    }

    private void Chkdsk_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync("Checking drive C:", ct => HealthService.RunChkdskScanAsync("C:", ct), ChkdskButton);
    }

    private void RestorePoint_Click(object sender, RoutedEventArgs e) =>
        _ = Ui.RunAsync("Creating restore point", ct => RestorePointService.CreateAsync("WindowsTuner manual restore point", ct), RestorePointButton);

    private void Memory_Click(object sender, RoutedEventArgs e) => HealthService.ScheduleMemoryDiagnostic();
    private void Security_Click(object sender, RoutedEventArgs e) => HealthService.OpenWindowsSecurity();
    private void SystemRestore_Click(object sender, RoutedEventArgs e) => RestorePointService.OpenSystemRestore();
    private void Reliability_Click(object sender, RoutedEventArgs e) => HealthService.OpenReliabilityMonitor();
}
