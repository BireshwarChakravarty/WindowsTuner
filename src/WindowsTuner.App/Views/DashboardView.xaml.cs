using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Diagnostics;
using WindowsTuner.Core.Drivers;
using WindowsTuner.Core.Security;
using WindowsTuner.Core.Health;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Startup;
using WindowsTuner.Core.SystemInfo;
using WindowsTuner.Core.Updates;

namespace WindowsTuner.App.Views;

public partial class DashboardView : UserControl, IPage
{
    private SystemSnapshot? _snapshot;

    public DashboardView() => InitializeComponent();

    public Task OnNavigatedToAsync() => LoadAsync(deep: false);

    private Task LoadAsync(bool deep) => Ui.RunAsync(deep ? "Checking for updates and drivers" : "Checking your PC", async ct =>
    {
        var s = await Task.Run(SystemInfoService.GetBasic, ct);
        if (_snapshot is not null)
        {
            // Keep results from an earlier deep scan.
            s.AppUpdatesAvailable = _snapshot.AppUpdatesAvailable;
            s.WindowsUpdatesAvailable = _snapshot.WindowsUpdatesAvailable;
        }
        Render(s);

        var cpu = SystemInfoService.SampleCpuAsync(TimeSpan.FromSeconds(1));
        var media = SystemInfoService.GetSystemDriveMediaAsync();
        var defender = HealthService.GetDefenderStatusAsync(ct);
        var disks = HealthService.GetDiskHealthAsync(ct);
        var startup = Task.Run(() => new StartupService().GetItems().Count(i => i.IsEnabled), ct);
        var devices = Task.Run(async () => (await new DriverService().GetProblemDevicesAsync(ct)).Count, ct);
        var events = EventLogReport.CollectAsync(ct);
        var security = SecurityCheckup.RunAsync(ct);
        var battery = BatteryReport.GetAsync(ct);
        var hardware = new DriverService().GetHardwareAsync();

        s.CpuLoadPercent = await cpu;
        s.SystemDriveMedia = await media;
        try { s.EnabledStartupApps = await startup; } catch { }
        try { s.ProblemDevices = await devices; } catch { }
        var d = await defender;
        if (d is not null)
        {
            s.DefenderRealTimeOn = d.RealTimeOn;
            s.DefenderSignatureAgeDays = d.SignatureAgeDays;
        }
        s.AnyDiskUnhealthy = (await disks).Any(x => !x.IsHealthy && x.Health != "Unknown");
        if (await events is { } ev)
        {
            s.BlueScreens = ev.BlueScreens;
            s.UnexpectedShutdowns = ev.UnexpectedShutdowns;
            s.DiskErrors = ev.DiskErrors + ev.FileSystemErrors;
            s.HardwareErrors = ev.HardwareErrors;
            s.BootSeconds = ev.LastBootSeconds;
        }
        var checks = await security;
        if (checks.Count > 0)
        {
            s.SecurityProblems = checks.Count(c => c.Status == CheckStatus.Bad);
            s.SecurityWarnings = checks.Count(c => c.Status == CheckStatus.Warning);
        }
        var bats = await battery;
        if (bats.Count > 0) s.BatteryHealthPercent = bats.Min(b => b.HealthPercent);
        s.BiosDate = (await hardware).BiosDate;

        if (deep)
        {
            var winget = new WingetService();
            try
            {
                s.AppUpdatesAvailable = (await winget.GetUpgradesAsync(App.Settings.IncludeUnknownVersions, ct))
                    .Count(a => !App.Settings.ExcludedAppIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) { Log.Warn(ex.Message); }
            try
            {
                s.WindowsUpdatesAvailable = (await new WindowsUpdateService().SearchAsync(App.Settings.IncludeDriverUpdates, false, ct)).Count;
            }
            catch (Exception ex) { Log.Warn("Windows Update check failed: " + ex.Message); }
        }

        _snapshot = s;
        Render(s);
    }, RefreshButton, DeepScanButton);

    private void Render(SystemSnapshot s)
    {
        OsText.Text = s.OsDisplay;
        UptimeText.Text = $"Up for {Format.Duration(s.Uptime)}" + (s.FastStartupEnabled ? " (Fast Startup on)" : "");
        RebootText.Text = s.PendingReboot ? "Restart needed to finish updates" : "";

        CpuName.Text = s.CpuName;
        CpuBar.Value = s.CpuLoadPercent;
        CpuText.Text = $"{s.CpuLoadPercent:0}% busy · {s.LogicalCpus} threads";

        var gb = 1024.0 * 1024 * 1024;
        RamText.Text = $"{(s.TotalRamBytes - s.AvailableRamBytes) / gb:0.0} of {s.TotalRamBytes / gb:0.0} GB used";
        RamBar.Value = s.MemoryLoadPercent;
        RamSub.Text = $"{s.MemoryLoadPercent}% in use";

        DiskTitle.Text = $"SYSTEM DRIVE ({s.SystemDrive}){(s.SystemDriveMedia.Length > 0 ? " · " + s.SystemDriveMedia : "")}";
        DiskText.Text = $"{Format.Bytes(s.SystemDriveFreeBytes)} free";
        DiskBar.Value = 100 - s.SystemDriveFreePercent;
        DiskSub.Text = $"of {Format.Bytes(s.SystemDriveTotalBytes)} ({s.SystemDriveFreePercent:0}% free)";

        StartupText.Text = s.EnabledStartupApps >= 0 ? $"🚀 {s.EnabledStartupApps} startup apps enabled" : "🚀 Startup apps: checking…";
        DefenderText.Text = s.DefenderRealTimeOn switch
        {
            true => $"🛡 Defender on · definitions {(s.DefenderSignatureAgeDays <= 0 ? "current" : s.DefenderSignatureAgeDays + " days old")}",
            false => "🛡 Defender real-time protection is OFF",
            _ => "🛡 Antivirus: checking…",
        };
        DevicesText.Text = s.ProblemDevices switch
        {
            < 0 => "🔌 Devices: checking…",
            0 => "🔌 All devices working",
            var n => $"🔌 {n} device(s) with problems",
        };
        UpdatesText.Text = s.AppUpdatesAvailable < 0
            ? "⬆ Click \"Check updates & drivers\""
            : $"⬆ {s.AppUpdatesAvailable} app · {Math.Max(0, s.WindowsUpdatesAvailable)} Windows/driver updates";

        var recs = RecommendationEngine.Evaluate(s);
        Recs.ItemsSource = recs;
        var score = RecommendationEngine.Score(recs);
        ScoreText.Text = score.ToString();
        ScoreText.Foreground = (System.Windows.Media.Brush)FindResource(score >= 80 ? "GoodBrush" : score >= 50 ? "WarnBrush" : "BadBrush");
        ScoreLabel.Text = score >= 80 ? "Health score · good" : score >= 50 ? "Health score · needs attention" : "Health score · needs work";
        Subtitle.Text = $"{s.ComputerName} · {recs.Count(r => r.Severity >= Severity.Warning)} thing(s) to look at";
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync(deep: false);
    private void DeepScan_Click(object sender, RoutedEventArgs e) => _ = LoadAsync(deep: true);
    private void TuneUp_Click(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo(FixPage.TuneUp);

    private void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FixPage page }) MainWindow.Current?.NavigateTo(page);
    }
}
