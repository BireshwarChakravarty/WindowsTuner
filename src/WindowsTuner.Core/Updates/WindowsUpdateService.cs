using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Updates;

public sealed class WindowsUpdateItem : ObservableObject
{
    private bool _isSelected;
    private UpdateStatus _status;
    private string _message = "";

    public required string Title { get; init; }
    public string KB { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool IsDriver { get; init; }
    /// <summary>Optional updates ("BrowseOnly") are not installed automatically by Windows.</summary>
    public bool IsOptional { get; init; }
    public string Severity { get; init; } = "";
    public string Categories { get; init; } = "";
    public string DriverManufacturer { get; init; } = "";
    public string DriverClass { get; init; } = "";
    public DateTime? DriverDate { get; init; }
    public string Description { get; init; } = "";

    public string Kind => IsDriver ? (IsOptional ? "Driver (optional)" : "Driver") : IsOptional ? "Optional" : "Windows";
    public string Size => SizeBytes > 0 ? Format.Bytes(SizeBytes) : "";

    /// <summary>The live COM IUpdate object (late bound).</summary>
    internal object? ComObject { get; init; }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public UpdateStatus Status { get => _status; set => Set(ref _status, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
}

/// <summary>
/// Windows Update (including driver updates published to Windows Update by hardware vendors) through the
/// Windows Update Agent COM API — the same engine the Settings app uses.
/// </summary>
public sealed class WindowsUpdateService
{
    public const string MicrosoftUpdateServiceId = "7971f918-a847-4430-9279-4a52d1efe18d";

    private dynamic? _session;

    private dynamic Session => _session ??= CreateSession();

    private static dynamic CreateSession()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
                   ?? throw new PlatformNotSupportedException("Windows Update Agent is not available.");
        dynamic session = Activator.CreateInstance(type)!;
        session.ClientApplicationID = "WindowsTuner";
        return session;
    }

    public Task<IReadOnlyList<WindowsUpdateItem>> SearchAsync(bool includeDrivers, bool includeOptional, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<WindowsUpdateItem>>(() =>
        {
            Log.Info("Searching Windows Update" + (includeDrivers ? " (including drivers)" : "") + "… this can take a few minutes on a PC that has been off for a while.");
            dynamic searcher = Session.CreateUpdateSearcher();
            searcher.Online = true;
            var criteria = includeDrivers ? "IsInstalled=0 and IsHidden=0" : "IsInstalled=0 and IsHidden=0 and Type='Software'";
            dynamic result = searcher.Search(criteria);
            ct.ThrowIfCancellationRequested();

            var items = new List<WindowsUpdateItem>();
            dynamic updates = result.Updates;
            int count = updates.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic u = updates[i];
                var item = Map(u);
                if (item.IsOptional && !includeOptional && !item.IsDriver) continue;
                // Microsoft's guidance for optional drivers: only install them if you have a problem with that device.
                item.IsSelected = !item.IsOptional;
                if (item.IsOptional && item.IsDriver) item.Message = "Optional — install if this device has problems";
                items.Add(item);
            }
            Log.Info($"Windows Update found {items.Count} update(s) ({items.Count(x => x.IsDriver)} driver).");
            return items;
        }, ct);

    private static WindowsUpdateItem Map(dynamic u)
    {
        string title = u.Title;
        int type = u.Type; // 1 = software, 2 = driver
        var kbs = new List<string>();
        try
        {
            dynamic ids = u.KBArticleIDs;
            int n = ids.Count;
            for (var k = 0; k < n; k++) kbs.Add("KB" + (string)ids[k]);
        }
        catch { /* drivers have none */ }

        var cats = new List<string>();
        try
        {
            dynamic c = u.Categories;
            int n = c.Count;
            for (var k = 0; k < n; k++) cats.Add((string)c[k].Name);
        }
        catch { }

        long size = 0;
        try { size = (long)Convert.ToDecimal(u.MaxDownloadSize); } catch { }

        bool browseOnly = false;
        try { browseOnly = u.BrowseOnly; } catch { }

        string severity = "";
        try { severity = u.MsrcSeverity ?? ""; } catch { }

        string manufacturer = "", driverClass = "";
        DateTime? driverDate = null;
        if (type == 2)
        {
            try { manufacturer = u.DriverManufacturer ?? ""; } catch { }
            try { driverClass = u.DriverClass ?? ""; } catch { }
            try { driverDate = (DateTime)u.DriverVerDate; } catch { }
        }

        string description = "";
        try { description = u.Description ?? ""; } catch { }

        return new WindowsUpdateItem
        {
            Title = title,
            KB = string.Join(", ", kbs),
            SizeBytes = size,
            IsDriver = type == 2,
            IsOptional = browseOnly,
            Severity = severity,
            Categories = string.Join(", ", cats),
            DriverManufacturer = manufacturer,
            DriverClass = driverClass,
            DriverDate = driverDate,
            Description = description,
            ComObject = u,
        };
    }

    /// <summary>Downloads and installs the selected updates one by one so a single failure doesn't block the rest.</summary>
    public Task<InstallSummary> InstallAsync(IReadOnlyList<WindowsUpdateItem> items, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var summary = new InstallSummary();
            var done = 0;
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (item.ComObject is null) continue;
                dynamic u = item.ComObject;
                try
                {
                    item.Status = UpdateStatus.Updating;
                    item.Message = "Downloading…";
                    Log.Info($"Windows Update: {item.Title}");
                    try { if (!(bool)u.EulaAccepted) u.AcceptEula(); } catch { }

                    var collType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl")!;
                    dynamic coll = Activator.CreateInstance(collType)!;
                    coll.Add(u);

                    if (!(bool)u.IsDownloaded)
                    {
                        dynamic downloader = Session.CreateUpdateDownloader();
                        downloader.Updates = coll;
                        dynamic dres = downloader.Download();
                        int dcode = dres.ResultCode;
                        if (dcode is not (2 or 3))
                            throw new InvalidOperationException($"Download failed ({ResultText(dcode)}, {Format.HResult((int)dres.HResult)})");
                    }

                    item.Message = "Installing…";
                    dynamic installer = Session.CreateUpdateInstaller();
                    installer.Updates = coll;
                    try { installer.AllowSourcePrompts = false; } catch { }
                    dynamic ires = installer.Install();
                    int code = ires.ResultCode;
                    bool reboot = ires.RebootRequired;
                    if (code is 2 or 3)
                    {
                        summary.Installed++;
                        summary.RebootRequired |= reboot;
                        item.Status = UpdateStatus.Updated;
                        item.Message = reboot ? "Installed — restart required" : "Installed";
                        Log.Success($"Installed: {item.Title}" + (reboot ? " (restart required)" : ""));
                    }
                    else
                    {
                        throw new InvalidOperationException($"Install {ResultText(code)} ({Format.HResult((int)ires.HResult)})");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    summary.Failed++;
                    item.Status = UpdateStatus.Failed;
                    item.Message = ex.Message;
                    Log.Error($"{item.Title}: {ex.Message}");
                }
                progress?.Report((++done, items.Count));
            }
            return summary;
        }, ct);

    private static string ResultText(int code) => code switch
    {
        0 => "not started",
        1 => "in progress",
        2 => "succeeded",
        3 => "succeeded with errors",
        4 => "failed",
        5 => "aborted",
        _ => $"result {code}",
    };

    /// <summary>"Receive updates for other Microsoft products" (Office, .NET, SQL, etc.).</summary>
    public static bool IsMicrosoftUpdateEnabled()
    {
        try
        {
            dynamic sm = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.ServiceManager")!)!;
            dynamic services = sm.Services;
            int n = services.Count;
            for (var i = 0; i < n; i++)
            {
                dynamic s = services[i];
                if (string.Equals((string)s.ServiceID, MicrosoftUpdateServiceId, StringComparison.OrdinalIgnoreCase))
                    return (bool)s.IsRegisteredWithAU;
            }
        }
        catch { }
        return false;
    }

    public static Task SetMicrosoftUpdateEnabledAsync(bool enabled) => Task.Run(() =>
    {
        dynamic sm = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.ServiceManager")!)!;
        sm.ClientApplicationID = "WindowsTuner";
        if (enabled)
        {
            // 7 = asfAllowPendingRegistration | asfAllowOnlineRegistration | asfRegisterServiceWithAU
            sm.AddService2(MicrosoftUpdateServiceId, 7, "");
            Log.Success("Enabled updates for other Microsoft products.");
        }
        else
        {
            sm.RemoveService(MicrosoftUpdateServiceId);
            Log.Success("Disabled updates for other Microsoft products.");
        }
    });

    public static void OpenSettings() => Shell.Open("ms-settings:windowsupdate");
    public static void OpenOptionalUpdates() => Shell.Open("ms-settings:windowsupdate-optionalupdates");
}

public sealed class InstallSummary
{
    public int Installed { get; set; }
    public int Failed { get; set; }
    public bool RebootRequired { get; set; }
}
