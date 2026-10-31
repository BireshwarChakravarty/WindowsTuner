using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Startup;

namespace WindowsTuner.App.Views;

public partial class StartupView : UserControl, IPage
{
    private readonly StartupService _service = new();
    private IReadOnlyList<StartupItem> _items = [];

    public StartupView() => InitializeComponent();

    public Task OnNavigatedToAsync() => LoadAsync();

    private Task LoadAsync() => Ui.RunAsync("Reading startup apps", async _ =>
    {
        _items = await Task.Run(_service.GetItems);
        Grid.ItemsSource = _items;
        UpdateSummary();
    }, RefreshButton);

    private void UpdateSummary() =>
        Summary.Text = $"{_items.Count(i => i.IsEnabled)} of {_items.Count} enabled";

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: StartupItem item } cb) return;
        try
        {
            _service.SetEnabled(item, cb.IsChecked == true);
        }
        catch (Exception ex)
        {
            Ui.Error($"Couldn't change {item.Name}: {ex.Message}");
        }
        cb.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.UpdateTarget();
        UpdateSummary();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();
    private void Settings_Click(object sender, RoutedEventArgs e) => StartupService.OpenStartupSettings();
}
