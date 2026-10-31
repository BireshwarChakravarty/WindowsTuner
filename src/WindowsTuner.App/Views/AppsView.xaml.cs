using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Apps;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Safety;

namespace WindowsTuner.App.Views;

public partial class AppsView : UserControl, IPage
{
    private IReadOnlyList<AppxPackageInfo> _all = [];

    public AppsView() => InitializeComponent();

    public Task OnNavigatedToAsync() => LoadAsync();

    private Task LoadAsync() => Ui.RunAsync("Listing preinstalled apps", async ct =>
    {
        _all = await AppxService.GetRemovablePackagesAsync(ct);
        ApplyFilter();
    }, RefreshButton, RemoveButton);

    private void ApplyFilter() =>
        Grid.ItemsSource = OnlySuggested.IsChecked == true
            ? _all.Where(a => a.Rating != BloatRating.Keep && !a.IsProtected).ToList()
            : _all;

    private void Filter_Click(object sender, RoutedEventArgs e) => ApplyFilter();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void SelectSuggested_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in _all) a.IsSelected = a.Rating == BloatRating.Suggested;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var selected = _all.Where(a => a.IsSelected && !a.IsProtected).ToList();
        if (selected.Count == 0) return;
        if (!Ui.Confirm($"Remove {selected.Count} app(s)?\n\n{string.Join("\n", selected.Select(s => "• " + s.FriendlyName))}\n\nYou can reinstall any of them from the Microsoft Store.")) return;
        var forNewUsers = ForNewUsers.IsChecked == true;
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync($"Removing {selected.Count} app(s)", async ct =>
        {
            if (App.Settings.CreateRestorePointBeforeChanges)
                await RestorePointService.CreateAsync("WindowsTuner: before removing apps", ct);
            var removed = 0;
            foreach (var pkg in selected)
            {
                ct.ThrowIfCancellationRequested();
                if (await AppxService.RemoveAsync(pkg, forNewUsers, ct)) removed++;
                pkg.IsSelected = false;
            }
            Log.Success($"Removed {removed} of {selected.Count} app(s).");
        }, RefreshButton, RemoveButton);
    }

    private void Programs_Click(object sender, RoutedEventArgs e) => Shell.Open("ms-settings:appsfeatures");
}
