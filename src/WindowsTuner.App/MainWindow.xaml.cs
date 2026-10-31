using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using WindowsTuner.App.Helpers;
using WindowsTuner.App.Views;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.SystemInfo;

namespace WindowsTuner.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LogEntry> _log = [];
    private readonly Dictionary<RadioButton, Func<UserControl>> _factories;
    private readonly Dictionary<RadioButton, UserControl> _pages = [];
    private readonly HashSet<UserControl> _loaded = [];

    public static MainWindow? Current { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Current = this;

        _factories = new()
        {
            [NavDashboard] = () => new DashboardView(),
            [NavTuneUp] = () => new TuneUpView(),
            [NavUpdates] = () => new UpdatesView(),
            [NavDrivers] = () => new DriversView(),
            [NavSecurity] = () => new SecurityView(),
            [NavBackground] = () => new BackgroundView(),
            [NavPrograms] = () => new ProgramsView(),
            [NavNetwork] = () => new NetworkView(),
            [NavCleanup] = () => new CleanupView(),
            [NavStartup] = () => new StartupView(),
            [NavApps] = () => new AppsView(),
            [NavPerformance] = () => new PerformanceView(),
            [NavTweaks] = () => new TweaksView(),
            [NavHealth] = () => new HealthView(),
            [NavSettings] = () => new SettingsView(),
        };

        LogList.ItemsSource = _log;
        Log.Written += entry => Dispatcher.BeginInvoke(() =>
        {
            _log.Add(entry);
            if (_log.Count > 3000) _log.RemoveAt(0);
            LogList.ScrollIntoView(entry);
            if (entry.Level != LogLevel.Output) StatusText.Text = entry.Message;
        });
        Ui.BusyChanged += () => Dispatcher.BeginInvoke(UpdateBusy);

        var admin = Elevation.IsAdministrator;
        AdminText.Text = admin ? "✔ Running as administrator" : "⚠ Not elevated — some features will fail. Restart as administrator.";
        AdminText.Foreground = (System.Windows.Media.Brush)FindResource(admin ? "GoodBrush" : "WarnBrush");
        VersionText.Text = $"Version {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)} · {Environment.MachineName}";

        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Loaded += (_, _) =>
        {
            NavDashboard.IsChecked = true;
            Show(NavDashboard);
        };
        Log.Info($"WindowsTuner started on {SystemInfoService.GetBasic().OsDisplay}");
    }

    private void UpdateBusy()
    {
        var busy = Ui.IsBusy;
        BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = busy ? Ui.Status + "…" : "Ready";
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb) Show(rb);
    }

    private async void Show(RadioButton nav)
    {
        if (!_pages.TryGetValue(nav, out var page))
        {
            page = _factories[nav]();
            _pages[nav] = page;
        }
        PageHost.Content = page;
        if (page is IPage p && _loaded.Add(page))
        {
            try { await p.OnNavigatedToAsync(); }
            catch (Exception ex) { Log.Error($"Loading {nav.Content} failed: {ex.Message}"); }
        }
    }

    /// <summary>Used by dashboard recommendations ("Fix" buttons).</summary>
    public void NavigateTo(FixPage target)
    {
        RadioButton? nav = target switch
        {
            FixPage.Updates => NavUpdates,
            FixPage.Drivers => NavDrivers,
            FixPage.Cleanup => NavCleanup,
            FixPage.Startup => NavStartup,
            FixPage.Tweaks => NavTweaks,
            FixPage.Performance => NavPerformance,
            FixPage.Health => NavHealth,
            FixPage.Apps => NavApps,
            FixPage.TuneUp => NavTuneUp,
            FixPage.Security => NavSecurity,
            FixPage.Network => NavNetwork,
            FixPage.Programs => NavPrograms,
            FixPage.Background => NavBackground,
            _ => null,
        };
        if (target == FixPage.Restart)
        {
            Ui.OfferRestart("Restarting finishes pending updates and clears memory.");
            return;
        }
        if (nav is null) return;
        nav.IsChecked = true;
        Show(nav);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Ui.CancelAll();

    private void LogToggle_Click(object sender, RoutedEventArgs e)
    {
        var show = LogPanel.Visibility != Visibility.Visible;
        LogPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LogToggle.Content = show ? "Hide activity log" : "Show activity log";
    }

    public void ShowLog()
    {
        LogPanel.Visibility = Visibility.Visible;
        LogToggle.Content = "Hide activity log";
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        if (!System.IO.File.Exists(Log.TodayLogFile)) Log.Info("Log file created.");
        Shell.Open(Log.TodayLogFile);
    }

    private void SaveScreenshot(string name)
    {
        try
        {
            var dir = System.IO.Path.Combine(AppPaths.Logs, "screenshots");
            System.IO.Directory.CreateDirectory(dir);
            var root = (FrameworkElement)Content;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)Math.Max(1, root.ActualWidth), (int)Math.Max(1, root.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            // Paint the window background first so the PNG isn't transparent.
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            }
            bmp.Render(visual);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var fs = System.IO.File.Create(System.IO.Path.Combine(dir, name));
            encoder.Save(fs);
        }
        catch (Exception ex)
        {
            Log.Warn("Screenshot failed: " + ex.Message);
        }
    }

    /// <summary>CI smoke test: render every page (read-only loads) and report whether anything threw.</summary>
    public async Task<bool> RunSmokeTestAsync()
    {
        var ok = true;
        foreach (var nav in _factories.Keys.ToList())
        {
            try
            {
                nav.IsChecked = true;
                var page = _factories[nav]();
                _pages[nav] = page;
                PageHost.Content = page;
                if (page is IPage p)
                {
                    var load = p.OnNavigatedToAsync();
                    await Task.WhenAny(load, Task.Delay(TimeSpan.FromSeconds(90)));
                    if (load.IsFaulted) throw load.Exception!.GetBaseException();
                }
                UpdateLayout();
                await Task.Delay(500);
                SaveScreenshot($"{_pages.Count:00}-{nav.Name}.png");
                Log.Success($"[smoke] {nav.Content} OK");
            }
            catch (Exception ex)
            {
                ok = false;
                Log.Error($"[smoke] {nav.Content} FAILED: {ex}");
            }
        }
        LogPanel.Visibility = Visibility.Visible;
        UpdateLayout();
        System.IO.File.WriteAllLines(System.IO.Path.Combine(AppPaths.Logs, "smoke.log"), _log.Select(l => l.ToString()));
        return ok;
    }
}
