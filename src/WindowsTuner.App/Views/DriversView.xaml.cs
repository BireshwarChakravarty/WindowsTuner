using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Drivers;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.SystemInfo;

namespace WindowsTuner.App.Views;

public partial class DriversView : UserControl, IPage
{
    private readonly DriverService _service = new();
    private IReadOnlyList<DriverRow> _drivers = [];

    public sealed record DriverRow(DriverInfo Info)
    {
        public string DeviceName => Info.DeviceName;
        public string DeviceClass => Info.DeviceClass;
        public string Provider => Info.Provider;
        public string Version => Info.Version;
        public string DateText => Info.DateText;
        public int? AgeYears => Info.IsInboxMicrosoftDriver ? null : Info.AgeYears;
        public bool Outdated => Info.LooksOutdated();
    }

    public DriversView() => InitializeComponent();

    public Task OnNavigatedToAsync() => LoadAsync();

    private Task LoadAsync() => Ui.RunAsync("Scanning drivers", async ct =>
    {
        var hw = await _service.GetHardwareAsync();
        HardwareText.Text = $"{hw.Manufacturer} {hw.Model}".Trim() +
                            (hw.BiosVersion.Length > 0 ? $" · BIOS {hw.BiosVersion}" + (hw.BiosDate is { } bd ? $" ({bd:yyyy-MM-dd})" : "") : "") +
                            (hw.Gpus.Count > 0 ? " · " + string.Join(", ", hw.Gpus) : "");
        VendorTools.ItemsSource = VendorToolAdvisor.Recommend(hw.Gpus, hw.Manufacturer, hw.CpuName);

        var problems = await _service.GetProblemDevicesAsync(ct);
        ProblemGrid.ItemsSource = problems;
        ProblemHeader.Text = problems.Count == 0 ? "Devices with problems — none 🎉" : $"Devices with problems ({problems.Count})";
        ProblemGrid.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        _drivers = (await _service.GetInstalledDriversAsync(ct)).Select(d => new DriverRow(d)).ToList();
        ApplyFilter();
    }, RefreshButton);

    private void ApplyFilter()
    {
        var showAll = ShowMicrosoft.IsChecked == true;
        var rows = _drivers.Where(d => showAll || !d.Info.IsInboxMicrosoftDriver)
            .OrderByDescending(d => d.Outdated).ThenBy(d => d.DeviceClass).ToList();
        DriverGrid.ItemsSource = rows;
        var old = rows.Count(r => r.Outdated);
        DriverSummary.Text = $"Installed drivers ({rows.Count})" + (old > 0 ? $" — {old} are 3+ years old (highlighted)" : "");
    }

    private void Filter_Click(object sender, RoutedEventArgs e) => ApplyFilter();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync("Backing up drivers", async ct =>
        {
            var dest = await DriverService.BackupDriversAsync(ct);
            if (dest is not null && Ui.Confirm($"Drivers saved to:\n{dest}\n\nOpen the folder?")) Shell.Open(dest);
        }, BackupButton);
    }

    private void WuDrivers_Click(object sender, RoutedEventArgs e) => MainWindow.Current?.NavigateTo(FixPage.Updates);

    private void ScanHw_Click(object sender, RoutedEventArgs e) =>
        _ = Ui.RunAsync("Scanning for hardware changes", async ct =>
        {
            await DriverService.ScanForHardwareChangesAsync(ct);
            await LoadAsync();
        }, ScanHwButton);

    private void DevMgr_Click(object sender, RoutedEventArgs e) => DriverService.OpenDeviceManager();

    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url }) Shell.Open(url);
    }
}
