using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Safety;
using WindowsTuner.Core.Updates;

namespace WindowsTuner.App.Views;

public partial class UpdatesView : UserControl, IPage
{
    private readonly WingetService _winget = new();
    private readonly WindowsUpdateService _wu = new();
    private ObservableCollection<AppUpdate> _apps = [];
    private ObservableCollection<WindowsUpdateItem> _updates = [];

    public UpdatesView()
    {
        InitializeComponent();
        IncludeUnknown.IsChecked = App.Settings.IncludeUnknownVersions;
        IncludeDrivers.IsChecked = App.Settings.IncludeDriverUpdates;
        IncludeOptional.IsChecked = App.Settings.IncludeOptionalUpdates;
    }

    public async Task OnNavigatedToAsync()
    {
        MicrosoftUpdate.IsChecked = await Task.Run(WindowsUpdateService.IsMicrosoftUpdateEnabled);
    }

    private UIElement[] AppButtons => [ScanAppsButton, UpdateSelectedButton, UpdateAllButton, SkipButton];

    private void ScanApps_Click(object sender, RoutedEventArgs e) => _ = ScanAppsAsync();

    private Task ScanAppsAsync() => Ui.RunAsync("Checking for app updates", async ct =>
    {
        App.Settings.IncludeUnknownVersions = IncludeUnknown.IsChecked == true;
        App.Settings.Save();
        if (!await _winget.IsAvailableAsync(ct))
        {
            WingetMissing.Visibility = Visibility.Visible;
            AppsSummary.Text = "winget is required for app updates.";
            return;
        }
        WingetMissing.Visibility = Visibility.Collapsed;
        await _winget.UpdateSourcesAsync(ct);
        var list = await _winget.GetUpgradesAsync(App.Settings.IncludeUnknownVersions, ct);
        foreach (var a in list)
        {
            var excluded = App.Settings.ExcludedAppIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase);
            a.IsSelected = !excluded && !a.RequiresExplicitTarget;
            if (excluded) a.Message = "On your skip list";
            else if (a.RequiresExplicitTarget) a.Message = "Pinned — select to update anyway";
        }
        _apps = new ObservableCollection<AppUpdate>(list);
        AppsGrid.ItemsSource = _apps;
        AppsSummary.Text = list.Count == 0
            ? "✔ All your apps are up to date."
            : $"{list.Count} app update(s) available. Close the apps you're updating — installers can't replace files that are in use.";
    }, AppButtons);

    private async void UpdateSelected_Click(object sender, RoutedEventArgs e) => await UpdateAppsAsync(_apps.Where(a => a.IsSelected).ToList());

    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        if (_apps.Count == 0) await ScanAppsAsync();
        await UpdateAppsAsync(_apps.Where(a => !App.Settings.ExcludedAppIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase)
                                                && a.Status != UpdateStatus.Updated).ToList());
    }

    private Task UpdateAppsAsync(List<AppUpdate> apps)
    {
        if (apps.Count == 0)
        {
            Ui.Info("Nothing selected to update. Click \"Check for app updates\" first.");
            return Task.CompletedTask;
        }
        MainWindow.Current?.ShowLog();
        return Ui.RunAsync($"Updating {apps.Count} app(s)", async ct =>
        {
            if (App.Settings.CreateRestorePointBeforeChanges && apps.Count > 3)
                await RestorePointService.CreateAsync("WindowsTuner: before app updates", ct);
            var progress = new Progress<(int Done, int Total)>(p => AppsSummary.Text = $"Updating… {p.Done} of {p.Total} done");
            var (ok, failed) = await _winget.UpgradeManyAsync(apps, progress, ct);
            AppsSummary.Text = $"Finished: {ok} updated" + (failed > 0 ? $", {failed} failed — see Status column and the activity log." : ".");
        }, AppButtons);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        var selected = AppsGrid.SelectedItems.OfType<AppUpdate>().Concat(_apps.Where(a => a.IsSelected)).Distinct().ToList();
        if (selected.Count == 0) return;
        if (!Ui.Confirm($"Never update these {selected.Count} app(s) automatically?\n\n" + string.Join("\n", selected.Select(s => "• " + s.Name)) +
                        "\n\nYou can remove them from the skip list in Settings.")) return;
        foreach (var a in selected)
        {
            if (!App.Settings.ExcludedAppIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase)) App.Settings.ExcludedAppIds.Add(a.Id);
            a.IsSelected = false;
            a.Message = "On your skip list";
        }
        App.Settings.Save();
    }

    private void Store_Click(object sender, RoutedEventArgs e) => _ = Ui.RunAsync("Updating Store apps", async ct =>
    {
        if (!await StoreUpdateService.TriggerStoreUpdatesAsync(ct)) StoreUpdateService.OpenStoreLibrary();
    }, StoreButton);

    private void GetWinget_Click(object sender, RoutedEventArgs e) => Shell.Open("ms-windows-store://pdp/?ProductId=9NBLGGH4NNS1");

    // ── Windows Update ──

    private void ScanWu_Click(object sender, RoutedEventArgs e) => _ = Ui.RunAsync("Searching Windows Update", async ct =>
    {
        App.Settings.IncludeDriverUpdates = IncludeDrivers.IsChecked == true;
        App.Settings.IncludeOptionalUpdates = IncludeOptional.IsChecked == true;
        App.Settings.Save();
        WuSummary.Text = "Searching… (this can take a few minutes)";
        var list = await _wu.SearchAsync(App.Settings.IncludeDriverUpdates, App.Settings.IncludeOptionalUpdates, ct);
        _updates = new ObservableCollection<WindowsUpdateItem>(list);
        WuGrid.ItemsSource = _updates;
        WuSummary.Text = list.Count == 0
            ? "✔ Windows is up to date."
            : $"{list.Count} update(s) found ({list.Count(u => u.IsDriver)} driver). Total download: {Format.Bytes(list.Sum(u => u.SizeBytes))}.";
    }, ScanWuButton, InstallWuButton);

    private void InstallWu_Click(object sender, RoutedEventArgs e)
    {
        var selected = _updates.Where(u => u.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Ui.Info("Nothing selected. Click \"Check Windows Update\" first.");
            return;
        }
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync($"Installing {selected.Count} Windows update(s)", async ct =>
        {
            if (App.Settings.CreateRestorePointBeforeChanges)
                await RestorePointService.CreateAsync("WindowsTuner: before Windows updates", ct);
            var progress = new Progress<(int Done, int Total)>(p => WuSummary.Text = $"Installing… {p.Done} of {p.Total} done");
            var summary = await _wu.InstallAsync(selected, progress, ct);
            WuSummary.Text = $"Finished: {summary.Installed} installed, {summary.Failed} failed." + (summary.RebootRequired ? " Restart required." : "");
            if (summary.RebootRequired) Ui.OfferRestart("Some updates need a restart to finish installing.");
        }, ScanWuButton, InstallWuButton);
    }

    private void MicrosoftUpdate_Click(object sender, RoutedEventArgs e)
    {
        var enable = MicrosoftUpdate.IsChecked == true;
        _ = Ui.RunAsync("Changing Microsoft Update setting", _ => WindowsUpdateService.SetMicrosoftUpdateEnabledAsync(enable), MicrosoftUpdate);
    }

    private void WuSettings_Click(object sender, RoutedEventArgs e) => WindowsUpdateService.OpenSettings();
    private void WuOptional_Click(object sender, RoutedEventArgs e) => WindowsUpdateService.OpenOptionalUpdates();
}
