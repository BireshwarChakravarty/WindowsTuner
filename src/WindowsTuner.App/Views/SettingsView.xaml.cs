using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Updates;

namespace WindowsTuner.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        var s = App.Settings;
        ThemeDark.IsChecked = s.Theme == ThemeChoice.Dark;
        ThemeLight.IsChecked = s.Theme == ThemeChoice.Light;
        ThemeSystem.IsChecked = s.Theme == ThemeChoice.System;
        Swatches.ItemsSource = ThemeManager.AccentPresets;
        CustomAccent.Text = s.AccentColor;
        RestorePoints.IsChecked = s.CreateRestorePointBeforeChanges;
        SystemMode.IsChecked = s.UseSystemAccount;
        PrivilegeText.Text = Elevation.IsAdministrator
            ? $"Running as: {Environment.UserName} (administrator, elevated){(s.UseSystemAccount ? " + SYSTEM for protected operations" : "")}"
            : "Running WITHOUT administrator rights — restart WindowsTuner as administrator.";
        Excluded.ItemsSource = s.ExcludedAppIds.ToList();
        DataPath.Text = $"Settings, logs, the tweak undo journal and backups are stored in {AppPaths.Root}";
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Theme = ThemeLight.IsChecked == true ? ThemeChoice.Light
            : ThemeSystem.IsChecked == true ? ThemeChoice.System : ThemeChoice.Dark;
        Save();
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string hex })
        {
            App.Settings.AccentColor = hex;
            CustomAccent.Text = hex;
            Save();
        }
    }

    private void CustomAccent_Click(object sender, RoutedEventArgs e)
    {
        var hex = CustomAccent.Text.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        if (ThemeManager.ParseColor(hex) is null)
        {
            Ui.Error("Enter a colour like #FF6600.");
            return;
        }
        App.Settings.AccentColor = hex;
        Save();
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.CreateRestorePointBeforeChanges = RestorePoints.IsChecked == true;
        App.Settings.UseSystemAccount = SystemMode.IsChecked == true;
        SystemRunner.Enabled = App.Settings.UseSystemAccount;
        App.Settings.Save();
    }

    private void Save()
    {
        App.Settings.Save();
        ThemeManager.Apply(App.Settings);
    }

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        var id = NewExclusion.Text.Trim();
        if (id.Length == 0 || App.Settings.ExcludedAppIds.Contains(id, StringComparer.OrdinalIgnoreCase)) return;
        App.Settings.ExcludedAppIds.Add(id);
        App.Settings.Save();
        Excluded.ItemsSource = App.Settings.ExcludedAppIds.ToList();
        NewExclusion.Text = "";
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (Excluded.SelectedItem is not string id) return;
        App.Settings.ExcludedAppIds.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        App.Settings.Save();
        Excluded.ItemsSource = App.Settings.ExcludedAppIds.ToList();
    }

    private void Module_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Current?.ShowLog();
        _ = Ui.RunAsync("Installing WinGet PowerShell module", async ct =>
        {
            var r = await new WingetService().InstallPowerShellModuleAsync(ct);
            if (r.Success) Log.Success("WinGet PowerShell module installed.");
            else Log.Error("Module install failed: " + r.StdErr.Trim());
        }, ModuleButton);
    }

    private void OpenData_Click(object sender, RoutedEventArgs e) => Shell.Open(AppPaths.Root);
    private void OpenLogs_Click(object sender, RoutedEventArgs e) => Shell.Open(AppPaths.Logs);
    private void OpenBackups_Click(object sender, RoutedEventArgs e) => Shell.Open(AppPaths.Backups);
}
