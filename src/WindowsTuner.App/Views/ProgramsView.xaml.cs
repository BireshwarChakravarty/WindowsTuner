using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Programs;
using WindowsTuner.Core.Storage;

namespace WindowsTuner.App.Views;

public partial class ProgramsView : UserControl, IPage
{
    private IReadOnlyList<InstalledProgram> _programs = [];
    private List<FoundFile> _files = [];

    public ProgramsView()
    {
        InitializeComponent();
        SortBox.ItemsSource = new[] { "Sort: name", "Sort: largest first", "Sort: oldest first", "Sort: newest first" };
        SortBox.SelectedIndex = 0;
    }

    public Task OnNavigatedToAsync() => LoadProgramsAsync();

    private Task LoadProgramsAsync() => Ui.RunAsync("Reading installed programs", async _ =>
    {
        _programs = await Task.Run(() => new InstalledProgramsService().GetPrograms());
        ApplyFilter();
    }, RefreshButton);

    private void ApplyFilter()
    {
        var q = Search.Text.Trim();
        IEnumerable<InstalledProgram> list = _programs.Where(p => q.Length == 0
            || p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase));
        list = SortBox.SelectedIndex switch
        {
            1 => list.OrderByDescending(p => p.SizeBytes),
            2 => list.OrderBy(p => p.InstallDate ?? DateTime.MaxValue),
            3 => list.OrderByDescending(p => p.InstallDate ?? DateTime.MinValue),
            _ => list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
        };
        var rows = list.ToList();
        ProgramGrid.ItemsSource = rows;
        ProgramSummary.Text = $"{rows.Count} programs · {Format.Bytes(rows.Sum(r => r.SizeBytes))} (as reported by installers)";
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Sort_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadProgramsAsync();

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramGrid.SelectedItem is not InstalledProgram p) return;
        if (!p.CanUninstall)
        {
            Ui.Info($"{p.Name} doesn't provide an uninstaller. Try Settings → Apps.");
            return;
        }
        if (!Ui.Confirm($"Uninstall {p.Name}?\n\nIts own uninstaller will open — follow its prompts.")) return;
        _ = Ui.RunAsync($"Uninstalling {p.Name}", async ct =>
        {
            await InstalledProgramsService.UninstallAsync(p, ct);
            await LoadProgramsAsync();
        }, UninstallButton);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ProgramGrid.SelectedItem is InstalledProgram { InstallLocation.Length: > 0 } p && System.IO.Directory.Exists(p.InstallLocation))
            Shell.Open(p.InstallLocation);
    }

    private void ScanFiles_Click(object sender, RoutedEventArgs e) => _ = Ui.RunAsync("Scanning your files", async ct =>
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var large = await Task.Run(() => LargeFileFinder.FindLargest(profile, 250L * 1024 * 1024, 100, ct), ct);
        var installers = await Task.Run(() => LargeFileFinder.FindOldInstallers(LargeFileFinder.DownloadsFolder, TimeSpan.FromDays(30)), ct);
        _files = installers.Concat(large.Where(l => installers.All(i => i.Path != l.Path))).ToList();
        FileGrid.ItemsSource = _files;
        FileSummary.Text = $"{_files.Count} file(s), {Format.Bytes(_files.Sum(f => f.SizeBytes))} in total. Tick the ones you don't need.";
    }, ScanFilesButton);

    private void Recycle_Click(object sender, RoutedEventArgs e)
    {
        var selected = _files.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) return;
        if (!Ui.Confirm($"Move {selected.Count} file(s) ({Format.Bytes(selected.Sum(f => f.SizeBytes))}) to the Recycle Bin?")) return;
        if (LargeFileFinder.RecycleFiles(selected.Select(f => f.Path)))
        {
            Log.Success($"Moved {selected.Count} file(s) to the Recycle Bin. Empty it on the Clean up page to free the space.");
            _files = _files.Except(selected).ToList();
            FileGrid.ItemsSource = _files;
        }
        else Ui.Error("Some files couldn't be moved (they may be open).");
    }

    private void ShowFile_Click(object sender, RoutedEventArgs e)
    {
        if (FileGrid.SelectedItem is FoundFile f) Shell.Start("explorer.exe", $"/select,\"{f.Path}\"");
    }
}
