using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Cleanup;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.App.Views;

public partial class CleanupView : UserControl, IPage
{
    private readonly CleanupService _service = new();
    private readonly IReadOnlyList<CleanupTarget> _targets = CleanupService.CreateDefaultTargets();

    public CleanupView()
    {
        InitializeComponent();
        var saved = App.Settings.CleanupTargets;
        foreach (var t in _targets)
        {
            t.IsSelected = saved.Count > 0 ? saved.Contains(t.Id) : t.DefaultSelected;
            t.PropertyChanged += Target_Changed;
        }
        Grid.ItemsSource = _targets;
    }

    private void Target_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupTarget.IsSelected) or nameof(CleanupTarget.SizeBytes))
            Dispatcher.BeginInvoke(UpdateTotal);
        if (e.PropertyName == nameof(CleanupTarget.IsSelected))
        {
            App.Settings.CleanupTargets = _targets.Where(t => t.IsSelected).Select(t => t.Id).ToList();
            App.Settings.Save();
        }
    }

    private void UpdateTotal()
    {
        var selected = _targets.Where(t => t.IsSelected && t.SizeBytes > 0).Sum(t => t.SizeBytes);
        var all = _targets.Where(t => t.SizeBytes > 0).Sum(t => t.SizeBytes);
        TotalText.Text = $"{Format.Bytes(selected)} selected of {Format.Bytes(all)} found";
    }

    public Task OnNavigatedToAsync() => AnalyzeAsync();

    private Task AnalyzeAsync() => Ui.RunAsync("Analyzing junk files", async ct =>
    {
        await _service.AnalyzeAsync(_targets, ct);
        UpdateTotal();
    }, AnalyzeButton, CleanButton);

    private void Analyze_Click(object sender, RoutedEventArgs e) => _ = AnalyzeAsync();

    private void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = _targets.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0) return;
        if (!Ui.Confirm($"Delete these files?\n\n{string.Join("\n", selected.Select(t => $"• {t.Name} ({t.SizeText})"))}")) return;
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync("Cleaning", async ct =>
        {
            var r = await _service.CleanAsync(selected, ct);
            UpdateTotal();
            Ui.Info($"Freed {Format.Bytes(r.BytesFreed)}." + (r.FilesSkipped > 0 ? $"\n{r.FilesSkipped} file(s) were in use and were skipped — that's normal." : ""));
        }, AnalyzeButton, CleanButton);
    }

    private void AnalyzeComponents_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync("Analyzing Windows components", ct => CleanupService.AnalyzeComponentStoreAsync(ct), AnalyzeComponentsButton, CleanComponentsButton);
    }

    private void CleanComponents_Click(object sender, RoutedEventArgs e)
    {
        if (!Ui.Confirm("Clean up superseded Windows components?\n\nThis takes 5–30 minutes. Updates installed so far can no longer be uninstalled.")) return;
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync("Cleaning Windows components", async ct =>
        {
            var r = await CleanupService.ComponentStoreCleanupAsync(ct);
            if (r.Success) Log.Success("Windows component cleanup finished.");
            else Log.Error($"DISM exited with {r.ExitCode}");
        }, AnalyzeComponentsButton, CleanComponentsButton);
    }

    private void DiskCleanup_Click(object sender, RoutedEventArgs e) => CleanupService.OpenDiskCleanup();
    private void StorageSense_Click(object sender, RoutedEventArgs e) => CleanupService.OpenStorageSense();
}
