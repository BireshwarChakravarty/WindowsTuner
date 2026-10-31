using System.Windows;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.App.Helpers;

/// <summary>Runs long operations with a global busy indicator, cancellation and friendly error handling.</summary>
public static class Ui
{
    private static readonly List<(string Status, CancellationTokenSource Cts)> Running = [];

    public static event Action? BusyChanged;

    public static bool IsBusy
    {
        get { lock (Running) return Running.Count > 0; }
    }

    public static string Status
    {
        get { lock (Running) return Running.Count > 0 ? Running[^1].Status : "Ready"; }
    }

    public static async Task RunAsync(string status, Func<CancellationToken, Task> work, params UIElement[] disable)
    {
        var cts = new CancellationTokenSource();
        var entry = (status, cts);
        lock (Running) Running.Add(entry);
        BusyChanged?.Invoke();
        foreach (var e in disable) e.IsEnabled = false;
        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Warn($"{status} — cancelled.");
        }
        catch (Exception ex)
        {
            Log.Error($"{status} failed: {ex.Message}");
            Error($"{status} failed:\n\n{ex.Message}");
        }
        finally
        {
            foreach (var e in disable) e.IsEnabled = true;
            lock (Running) Running.Remove(entry);
            cts.Dispose();
            BusyChanged?.Invoke();
        }
    }

    public static void CancelAll()
    {
        lock (Running)
        {
            foreach (var (_, cts) in Running)
            {
                try { cts.Cancel(); } catch (ObjectDisposedException) { }
            }
        }
    }

    public static bool Confirm(string message, string title = "WindowsTuner") =>
        !App.IsSmokeTest && MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static void Info(string message, string title = "WindowsTuner")
    {
        if (App.IsSmokeTest) return;
        MessageBox.Show(Application.Current.MainWindow!, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static void Error(string message)
    {
        if (App.IsSmokeTest) return;
        MessageBox.Show(Application.Current.MainWindow!, message, "WindowsTuner", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Asks to restart Windows (after saving work).</summary>
    public static void OfferRestart(string reason)
    {
        if (Confirm($"{reason}\n\nRestart now? Save your work in other apps first."))
            Shell.Start("shutdown.exe", "/r /t 5 /c \"WindowsTuner: restarting to finish updates and changes\"");
    }
}

/// <summary>Pages that load data the first time they are shown.</summary>
public interface IPage
{
    Task OnNavigatedToAsync();
}
