using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Health;
using WindowsTuner.Core.Security;

namespace WindowsTuner.App.Views;

public partial class SecurityView : UserControl, IPage
{
    private IReadOnlyList<SecurityCheck> _checks = [];

    public SecurityView() => InitializeComponent();

    public Task OnNavigatedToAsync() => RunAsync();

    private Task RunAsync() => Ui.RunAsync("Running security checkup", async ct =>
    {
        _checks = await SecurityCheckup.RunAsync(ct);
        Checks.ItemsSource = _checks;
        UpdateSummary();
    }, RunButton);

    private void UpdateSummary()
    {
        var bad = _checks.Count(c => c.Status == CheckStatus.Bad);
        var warn = _checks.Count(c => c.Status == CheckStatus.Warning);
        Summary.Text = bad + warn == 0 ? "✔ Everything checked looks good." : $"{bad} problem(s), {warn} suggestion(s)";
    }

    private void Run_Click(object sender, RoutedEventArgs e) => _ = RunAsync();
    private void Security_Click(object sender, RoutedEventArgs e) => HealthService.OpenWindowsSecurity();

    private void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SecurityCheck check }) return;
        _ = Ui.RunAsync($"Fixing {check.Name}", async ct =>
        {
            await SecurityCheckup.FixAsync(check, ct);
            Checks.Items.Refresh();
            UpdateSummary();
        });
    }
}
