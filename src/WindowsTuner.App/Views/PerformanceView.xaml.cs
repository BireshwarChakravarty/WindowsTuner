using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Performance;

namespace WindowsTuner.App.Views;

public partial class PerformanceView : UserControl, IPage
{
    public PerformanceView() => InitializeComponent();

    public async Task OnNavigatedToAsync()
    {
        await LoadPlansAsync();
        await RefreshProcessesAsync();
    }

    private async Task LoadPlansAsync() => Plans.ItemsSource = await PowerPlanService.GetPlansAsync();

    private Task RefreshProcessesAsync() => Ui.RunAsync("Measuring CPU and memory use", async ct =>
    {
        ProcGrid.ItemsSource = await ProcessMonitor.SnapshotAsync(TimeSpan.FromSeconds(1.5), ct);
    }, RefreshProcButton);

    private void RefreshProc_Click(object sender, RoutedEventArgs e) => _ = RefreshProcessesAsync();

    private void End_Click(object sender, RoutedEventArgs e)
    {
        if (ProcGrid.SelectedItem is not ProcessUsage p) return;
        if (!Ui.Confirm($"End every \"{p.Name}\" process?\n\nUnsaved work in that app will be lost.")) return;
        ProcessMonitor.KillByName(p.Name);
        _ = RefreshProcessesAsync();
    }

    private async void Plan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid g })
        {
            await PowerPlanService.SetActiveAsync(g);
            await LoadPlansAsync();
        }
    }

    private async void Ultimate_Click(object sender, RoutedEventArgs e)
    {
        var id = await PowerPlanService.AddUltimatePerformanceAsync();
        if (id is { } g && Ui.Confirm("Ultimate Performance was added. Switch to it now?")) await PowerPlanService.SetActiveAsync(g);
        await LoadPlansAsync();
    }

    private void Optimize_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync("Optimizing drives", ct => DriveOptimizer.OptimizeAllAsync(ct), OptimizeButton);
    }

    private void PowerOptions_Click(object sender, RoutedEventArgs e) => PowerPlanService.OpenPowerOptions();
    private void OptimizeApp_Click(object sender, RoutedEventArgs e) => DriveOptimizer.OpenOptimizeDrives();
    private void VisualFx_Click(object sender, RoutedEventArgs e) => Shell.Start("SystemPropertiesPerformance.exe", "");
    private void Graphics_Click(object sender, RoutedEventArgs e) => Shell.Open("ms-settings:display-advancedgraphics");
    private void TaskMgr_Click(object sender, RoutedEventArgs e) => Shell.Start("taskmgr.exe", "");
    private void ResMon_Click(object sender, RoutedEventArgs e) => Shell.Start("resmon.exe", "");
}
