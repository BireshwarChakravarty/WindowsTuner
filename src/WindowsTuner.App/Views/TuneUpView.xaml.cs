using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Maintenance;

namespace WindowsTuner.App.Views;

public partial class TuneUpView : UserControl, IPage
{
    private readonly IReadOnlyList<TuneUpStep> _steps = TuneUpSteps.Create(App.Settings.TuneUpSteps);
    private bool _loading = true;

    public TuneUpView()
    {
        InitializeComponent();
        StepsGrid.ItemsSource = _steps;
        foreach (var s in _steps) s.PropertyChanged += Step_Changed;

        DayBox.ItemsSource = Enum.GetValues<DayOfWeek>();
        DayBox.SelectedItem = App.Settings.ScheduleDay;
        HourBox.ItemsSource = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList();
        HourBox.SelectedIndex = Math.Clamp(App.Settings.ScheduleHour, 0, 23);
    }

    public async Task OnNavigatedToAsync()
    {
        ScheduleEnabled.IsChecked = await Scheduler.IsEnabledAsync();
        _loading = false;
    }

    private void Step_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TuneUpStep.IsSelected)) return;
        App.Settings.TuneUpSteps = _steps.Where(s => s.IsSelected).Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        App.Settings.Save();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!_steps.Any(s => s.IsSelected)) return;
        if (!Ui.Confirm("Start the tune-up?\n\nClose apps you're working in — app updates may close them. This can take from a few minutes to an hour depending on how many updates are waiting. You can keep using the PC.")) return;
        MainWindow.Current?.ShowLog();
        foreach (var s in _steps)
        {
            s.Outcome = StepOutcome.Pending;
            s.Detail = "";
        }
        ResultText.Text = "";
        _ = Ui.RunAsync("Tune-up", async ct =>
        {
            var result = await new TuneUpRunner(App.Settings).RunAsync(_steps, ct);
            ResultText.Text = $"Done: {Format.Bytes(result.BytesFreed)} freed · {result.AppsUpdated} app(s) updated · {result.WindowsUpdatesInstalled} Windows/driver update(s)" +
                              (result.Failed > 0 ? $" · {result.Failed} step(s) failed" : "");
            RestartButton.Visibility = result.RebootRequired ? Visibility.Visible : Visibility.Collapsed;
            if (result.RebootRequired) Ui.OfferRestart("Updates were installed that need a restart to finish.");
        }, StartButton);
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => Ui.OfferRestart("Restart to finish installing updates.");

    private void Schedule_Click(object sender, RoutedEventArgs e) => _ = ApplyScheduleAsync();

    private void ScheduleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        App.Settings.ScheduleDay = DayBox.SelectedItem is DayOfWeek d ? d : DayOfWeek.Sunday;
        App.Settings.ScheduleHour = Math.Max(0, HourBox.SelectedIndex);
        App.Settings.Save();
        if (ScheduleEnabled.IsChecked == true) _ = ApplyScheduleAsync();
    }

    private Task ApplyScheduleAsync() => Ui.RunAsync("Updating schedule", async _ =>
    {
        var exe = Environment.ProcessPath ?? "WindowsTuner.exe";
        var ok = ScheduleEnabled.IsChecked == true
            ? await Scheduler.EnableAsync(exe, App.Settings.ScheduleDay, App.Settings.ScheduleHour)
            : await Scheduler.DisableAsync();
        App.Settings.ScheduledTuneUpEnabled = ScheduleEnabled.IsChecked == true && ok;
        App.Settings.Save();
        ScheduleEnabled.IsChecked = await Scheduler.IsEnabledAsync();
    }, ScheduleEnabled);
}
