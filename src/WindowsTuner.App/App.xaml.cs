using System.IO;
using System.Windows;
using System.Windows.Threading;
using WindowsTuner.App.Helpers;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Maintenance;

namespace WindowsTuner.App;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>--smoke: open every page and exit (used by CI to catch XAML/runtime errors).</summary>
    public static bool IsSmokeTest { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings = AppSettings.Load();
        DispatcherUnhandledException += OnUnhandled;

        var args = e.Args.Select(a => a.ToLowerInvariant()).ToList();

        if (args.Contains("--tune-up"))
        {
            // Headless weekly run from Task Scheduler.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = 0;
            try
            {
                var steps = TuneUpSteps.Create(Settings.TuneUpSteps);
                var result = await new TuneUpRunner(Settings).RunAsync(steps);
                code = result.Failed > 0 ? 2 : 0;
            }
            catch (Exception ex)
            {
                Log.Error("Scheduled tune-up crashed: " + ex);
                code = 1;
            }
            Shutdown(code);
            return;
        }

        IsSmokeTest = args.Contains("--smoke");
        ThemeManager.Apply(Settings);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (IsSmokeTest)
        {
            var ok = await window.RunSmokeTestAsync();
            Shutdown(ok ? 0 : 1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unexpected error: " + e.Exception);
        try { File.AppendAllText(Path.Combine(AppPaths.Logs, "crash.log"), $"{DateTime.Now:u} {e.Exception}\n\n"); } catch { }
        if (IsSmokeTest)
        {
            Shutdown(1);
            return;
        }
        MessageBox.Show($"Something went wrong:\n\n{e.Exception.Message}\n\nDetails were written to the log.", "WindowsTuner",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
