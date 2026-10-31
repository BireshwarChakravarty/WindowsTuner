using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Safety;
using WindowsTuner.Core.SystemInfo;
using WindowsTuner.Core.Tweaks;

namespace WindowsTuner.App.Views;

public partial class TweaksView : UserControl, IPage
{
    private const string All = "All tweaks", Recommended = "★ Recommended", Active = "Turned on", Custom = "Custom";

    private readonly TweakEngine _engine = new(WindowsRegistry.Instance);
    private readonly int _build = SystemInfoService.GetBuild();
    private List<TweakItem> _items = [];
    private RestartNeed _pendingRestart = RestartNeed.None;

    public TweaksView() => InitializeComponent();

    public Task OnNavigatedToAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var catalog = await Task.Run(() => TweakEngine.LoadCatalog());
        var items = catalog.Select(d => new TweakItem(d)).ToList();
        await Task.Run(() => { foreach (var i in items) _engine.Refresh(i, _build); });
        _items = items;

        var selected = Categories.SelectedItem as string ?? All;
        var cats = new List<string> { All, Recommended, Active };
        cats.AddRange(_items.Select(i => i.Category).Distinct().OrderBy(c => c));
        if (_items.Any(i => i.IsCustom)) cats.Add(Custom);
        Categories.ItemsSource = cats;
        Categories.SelectedItem = cats.Contains(selected) ? selected : All;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var cat = Categories.SelectedItem as string ?? All;
        var q = Search.Text.Trim();
        var advanced = ShowAdvanced.IsChecked == true;
        TweakList.ItemsSource = _items
            .Where(i => advanced || i.Risk != TweakRisk.Advanced || i.IsOn || cat == Active)
            .Where(i => cat switch
            {
                All => true,
                Recommended => i.IsRecommended,
                Active => i.State is TweakState.On or TweakState.Partial,
                Custom => i.IsCustom,
                _ => i.Category == cat,
            })
            .Where(i => q.Length == 0 || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || i.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || i.Category.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.IsApplicable ? 0 : 1).ThenBy(i => i.Category).ThenBy(i => i.Risk)
            .ToList();
    }

    private void Categories_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Filter_Click(object sender, RoutedEventArgs e) => ApplyFilter();
    private void Reload_Click(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: TweakItem item } cb) return;
        var turnOn = item.State != TweakState.On;
        // Undo the visual toggle; the OneWay binding updates the switch once the change is really applied.
        cb.GetBindingExpression(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.UpdateTarget();

        if (turnOn && item.Risk == TweakRisk.Advanced &&
            !Ui.Confirm($"\"{item.Name}\" is an advanced tweak.\n\n{item.Description}\n\nTurn it on?")) return;

        _ = Ui.RunAsync(item.Name, async ct =>
        {
            item.IsBusy = true;
            try
            {
                await _engine.ApplyAsync(item.Definition, turnOn, ct);
            }
            catch (UnauthorizedAccessException)
            {
                Log.Error($"{item.Name}: access denied. Windows protects this setting on your build, or WindowsTuner isn't running as administrator.");
            }
            finally
            {
                _engine.Refresh(item, _build);
                item.IsBusy = false;
            }
            NoteRestart(item.Restart);
        });
    }

    private void NoteRestart(RestartNeed need)
    {
        if (need > _pendingRestart) _pendingRestart = need;
        if (_pendingRestart == RestartNeed.None) return;
        RestartBanner.Visibility = Visibility.Visible;
        RestartExplorerButton.Visibility = _pendingRestart == RestartNeed.Explorer ? Visibility.Visible : Visibility.Collapsed;
        RebootButton.Visibility = _pendingRestart >= RestartNeed.SignOut ? Visibility.Visible : Visibility.Collapsed;
        RestartText.Text = _pendingRestart switch
        {
            RestartNeed.Explorer => "Some changes show up after Explorer restarts (your open windows stay open; the taskbar blinks).",
            RestartNeed.SignOut => "Some changes take effect after you sign out and back in.",
            _ => "Some changes take effect after restarting your PC.",
        };
    }

    private void Recommended_Click(object sender, RoutedEventArgs e)
    {
        var todo = _items.Where(i => i.IsRecommended && i.IsApplicable && !i.IsOn).ToList();
        if (todo.Count == 0)
        {
            Ui.Info("All recommended tweaks are already on.");
            return;
        }
        if (!Ui.Confirm($"Turn on {todo.Count} recommended tweak(s)?\n\n{string.Join("\n", todo.Select(t => "• " + t.Name))}")) return;
        _ = Ui.RunAsync("Applying recommended tweaks", async ct =>
        {
            if (App.Settings.CreateRestorePointBeforeChanges)
                await RestorePointService.CreateAsync("WindowsTuner: before recommended tweaks", ct);
            foreach (var t in todo)
            {
                try { await _engine.ApplyAsync(t.Definition, true, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException) { Log.Error($"{t.Name}: {ex.Message}"); }
                _engine.Refresh(t, _build);
                NoteRestart(t.Restart);
            }
            ApplyFilter();
        }, RecommendedButton);
    }

    private void UndoAll_Click(object sender, RoutedEventArgs e)
    {
        var count = _engine.JournaledTweakIds.Count;
        if (count == 0)
        {
            Ui.Info("There are no WindowsTuner tweaks to undo.");
            return;
        }
        if (!Ui.Confirm($"Restore the original values for all {count} tweak(s) WindowsTuner has turned on?")) return;
        _ = Ui.RunAsync("Undoing tweaks", async ct =>
        {
            await _engine.RevertAllAsync(_items.Select(i => i.Definition), ct);
            foreach (var i in _items) _engine.Refresh(i, _build);
            NoteRestart(RestartNeed.SignOut);
            ApplyFilter();
        }, UndoAllButton);
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save tweak profile",
            Filter = "WindowsTuner profile (*.json)|*.json",
            InitialDirectory = AppPaths.ProfilesDir,
            FileName = $"{Environment.MachineName}-tweaks.json",
        };
        if (dlg.ShowDialog() != true) return;
        var profile = _engine.CreateProfile(_items, Path.GetFileNameWithoutExtension(dlg.FileName));
        JsonFile.Write(dlg.FileName, profile);
        Log.Success($"Saved profile with {profile.Tweaks.Count(t => t.Value)} active tweak(s) to {dlg.FileName}");
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load tweak profile",
            Filter = "WindowsTuner profile (*.json)|*.json",
            InitialDirectory = AppPaths.ProfilesDir,
        };
        if (dlg.ShowDialog() != true) return;
        var profile = JsonFile.Read<TweakProfile>(dlg.FileName);
        if (profile is null)
        {
            Ui.Error("That file isn't a valid WindowsTuner profile.");
            return;
        }
        var changes = _items.Count(i => i.IsApplicable && profile.Tweaks.TryGetValue(i.Id, out var on) && on != i.IsOn);
        if (!Ui.Confirm($"Profile \"{profile.Name}\" will change {changes} tweak(s) on this PC. Continue?")) return;
        _ = Ui.RunAsync("Applying profile", async ct =>
        {
            if (App.Settings.CreateRestorePointBeforeChanges)
                await RestorePointService.CreateAsync($"WindowsTuner: before profile {profile.Name}", ct);
            var n = await _engine.ApplyProfileAsync(profile, _items, _build, ct);
            Log.Success($"Profile applied: {n} tweak(s) changed.");
            NoteRestart(RestartNeed.SignOut);
            ApplyFilter();
        });
    }

    private void Custom_Click(object sender, RoutedEventArgs e)
    {
        var path = AppPaths.CustomTweaksFile;
        if (!File.Exists(path))
        {
            File.WriteAllText(path, """
                // Your own tweaks. Same format as WindowsTuner's built-in catalog.
                // Save this file, then click "Reload" on the Tweaks page.
                //   hive: HKCU | HKLM | HKU | HKCR      kind: DWord | QWord | String | ExpandString | Binary | MultiString
                //   on:  value for the "on" state       off: value for "off" (null = delete the value)
                //   risk: Safe | Moderate | Advanced    restart: None | Explorer | SignOut | Reboot
                {
                  "version": 1,
                  "tweaks": [
                    {
                      "id": "custom.example.disableAeroShake",
                      "category": "Custom",
                      "name": "Disable Aero Shake",
                      "description": "Shaking a window's title bar no longer minimizes all other windows.",
                      "risk": "Safe",
                      "restart": "Explorer",
                      "registry": [
                        { "hive": "HKCU", "path": "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced",
                          "name": "DisallowShaking", "kind": "DWord", "on": 1, "off": null }
                      ]
                    }
                  ]
                }
                """);
        }
        Shell.Start("notepad.exe", $"\"{path}\"");
    }

    private void RestartExplorer_Click(object sender, RoutedEventArgs e) => _ = Ui.RunAsync("Restarting Explorer", async _ =>
    {
        await TweakEngine.RestartExplorerAsync();
        _pendingRestart = RestartNeed.None;
        RestartBanner.Visibility = Visibility.Collapsed;
    });

    private void Reboot_Click(object sender, RoutedEventArgs e) => Ui.OfferRestart("Your tweaks will take full effect after a restart.");
}
