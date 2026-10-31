using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Background;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.App.Views;

public partial class BackgroundView : UserControl, IPage
{
    public static readonly string[] StartModes = ["Automatic", "Manual", "Disabled"];

    private IReadOnlyList<ScheduledTaskInfo> _tasks = [];
    private IReadOnlyList<ServiceInfo> _services = [];
    private bool _loadingServices;

    public BackgroundView() => InitializeComponent();

    public async Task OnNavigatedToAsync()
    {
        await LoadTasksAsync();
        await LoadServicesAsync();
    }

    private Task LoadTasksAsync() => Ui.RunAsync("Reading scheduled tasks", async ct =>
    {
        _tasks = await BackgroundService.GetThirdPartyTasksAsync(ct);
        TaskGrid.ItemsSource = _tasks;
        TaskSummary.Text = $"{_tasks.Count(t => t.IsEnabled)} of {_tasks.Count} third-party tasks enabled";
    }, RefreshTasksButton);

    private Task LoadServicesAsync() => Ui.RunAsync("Reading services", async ct =>
    {
        _loadingServices = true;
        _services = await BackgroundService.GetThirdPartyServicesAsync(ct);
        ServiceGrid.ItemsSource = _services;
        await Dispatcher.InvokeAsync(() => _loadingServices = false, System.Windows.Threading.DispatcherPriority.Background);
    }, RefreshServicesButton);

    private void RefreshTasks_Click(object sender, RoutedEventArgs e) => _ = LoadTasksAsync();
    private void RefreshServices_Click(object sender, RoutedEventArgs e) => _ = LoadServicesAsync();

    private void TaskToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: ScheduledTaskInfo task } cb) return;
        var enable = !task.IsEnabled;
        cb.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateTarget();
        if (task.IsProtected)
        {
            Ui.Info("That's WindowsTuner's own weekly tune-up — turn it off on the Tune-up page.");
            return;
        }
        _ = Ui.RunAsync(enable ? "Enabling task" : "Disabling task", async ct =>
        {
            await BackgroundService.SetTaskEnabledAsync(task, enable, ct);
            TaskSummary.Text = $"{_tasks.Count(t => t.IsEnabled)} of {_tasks.Count} third-party tasks enabled";
        });
    }

    private void StartMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingServices || e.AddedItems.Count == 0 || e.RemovedItems.Count == 0) return;
        if (sender is not ComboBox { DataContext: ServiceInfo svc } box || box.SelectedItem is not string mode || mode == svc.StartMode) return;
        if (mode == "Disabled" && svc.Rating == ServiceRating.Keep &&
            !Ui.Confirm($"{svc.DisplayName} looks like security or hardware software. Disabling it may break that device or app. Continue?"))
        {
            box.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget();
            return;
        }
        _ = Ui.RunAsync($"Changing {svc.DisplayName}", async ct =>
        {
            await BackgroundService.SetStartModeAsync(svc, mode, ct);
            box.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget();
        });
    }

    private void UpdatersManual_Click(object sender, RoutedEventArgs e)
    {
        var updaters = _services.Where(s => s.Rating == ServiceRating.Updater && s.StartMode == "Automatic").ToList();
        if (updaters.Count == 0)
        {
            Ui.Info("No third-party updater services are set to start automatically.");
            return;
        }
        if (!Ui.Confirm($"Set {updaters.Count} updater service(s) to Manual?\n\n{string.Join("\n", updaters.Select(u => "• " + u.DisplayName))}\n\nThe apps still update — the service just isn't running all the time.")) return;
        _ = Ui.RunAsync("Setting updaters to Manual", async ct =>
        {
            foreach (var u in updaters) await BackgroundService.SetStartModeAsync(u, "Manual", ct);
            ServiceGrid.Items.Refresh();
        }, UpdatersManualButton);
    }

    private void RestoreServices_Click(object sender, RoutedEventArgs e)
    {
        var count = BackgroundService.LoadJournal().Count;
        if (count == 0)
        {
            Ui.Info("WindowsTuner hasn't changed any services.");
            return;
        }
        if (!Ui.Confirm($"Restore the original start type of {count} service(s)?")) return;
        _ = Ui.RunAsync("Restoring services", async ct =>
        {
            await BackgroundService.RestoreAllAsync(_services, ct);
            ServiceGrid.Items.Refresh();
        }, RestoreServicesButton);
    }

    private void TaskScheduler_Click(object sender, RoutedEventArgs e) => Shell.Start("taskschd.msc", "");
    private void ServicesConsole_Click(object sender, RoutedEventArgs e) => Shell.Start("services.msc", "");
}
