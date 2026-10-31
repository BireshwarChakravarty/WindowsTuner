using WindowsTuner.Core.Background;
using WindowsTuner.Core.Diagnostics;
using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Maintenance;
using WindowsTuner.Core.Network;
using WindowsTuner.Core.Programs;
using WindowsTuner.Core.Security;
using WindowsTuner.Core.Storage;
using WindowsTuner.Core.SystemInfo;

namespace WindowsTuner.Tests;

public class ThreeSixtyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wt-360").FullName;

    public ThreeSixtyTests() => Log.FileLoggingEnabled = false;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void DnsQueryPacketIsWellFormed()
    {
        var q = DnsBenchmark.BuildQuery(0xBEEF, "www.example.com");
        Assert.Equal(0xBE, q[0]);
        Assert.Equal(0xEF, q[1]);
        Assert.Equal(0x01, q[2]);             // RD flag
        Assert.Equal(1, q[5]);                // QDCOUNT = 1
        Assert.Equal(3, q[12]);               // "www"
        Assert.Equal((byte)'w', q[13]);
        Assert.Equal(new byte[] { 0, 0, 1, 0, 1 }, q[^5..]); // root label, type A, class IN
        Assert.Throws<ArgumentException>(() => DnsBenchmark.BuildQuery(1, "bad..name"));

        var response = new byte[12];
        response[0] = 0xBE; response[1] = 0xEF; response[2] = 0x81;
        Assert.True(DnsBenchmark.IsValidResponse(response, 0xBEEF));
        Assert.False(DnsBenchmark.IsValidResponse(response, 0x1234));
        response[2] = 0x01; // not a response
        Assert.False(DnsBenchmark.IsValidResponse(response, 0xBEEF));
    }

    [Fact]
    public void DnsProvidersHaveIPv4AndIPv6()
    {
        Assert.All(DnsBenchmark.Providers, p =>
        {
            Assert.Contains(p.Servers, s => System.Net.IPAddress.TryParse(s, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            Assert.Contains(p.Servers, s => s.Contains(':'));
        });
    }

    [Fact]
    public void ReadsInstalledProgramsFromRegistry()
    {
        var reg = new InMemoryRegistry();
        const string u = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        reg.SetValue(RegHive.HKLM, u + @"\{GUID-1}", "DisplayName", "7-Zip 24.08", RegKind.String);
        reg.SetValue(RegHive.HKLM, u + @"\{GUID-1}", "Publisher", "Igor Pavlov", RegKind.String);
        reg.SetValue(RegHive.HKLM, u + @"\{GUID-1}", "EstimatedSize", 5000, RegKind.DWord);
        reg.SetValue(RegHive.HKLM, u + @"\{GUID-1}", "InstallDate", "20240115", RegKind.String);
        reg.SetValue(RegHive.HKLM, u + @"\{GUID-1}", "UninstallString", "\"C:\\Program Files\\7-Zip\\Uninstall.exe\"", RegKind.String);
        reg.SetValue(RegHive.HKLM, u + @"\Hidden", "DisplayName", "Some runtime", RegKind.String);
        reg.SetValue(RegHive.HKLM, u + @"\Hidden", "SystemComponent", 1, RegKind.DWord);
        reg.SetValue(RegHive.HKLM, u + @"\Patch", "DisplayName", "Update for X", RegKind.String);
        reg.SetValue(RegHive.HKLM, u + @"\Patch", "ParentKeyName", "X", RegKind.String);
        reg.SetValue(RegHive.HKCU, u + @"\Discord", "DisplayName", "Discord", RegKind.String);
        reg.SetValue(RegHive.HKLM, u + @"\NoName", "Publisher", "Nobody", RegKind.String);

        var programs = new InstalledProgramsService(reg).GetPrograms();
        Assert.Equal(["7-Zip 24.08", "Discord"], programs.Select(p => p.Name).ToArray());
        var zip = programs[0];
        Assert.Equal(5000L * 1024, zip.SizeBytes);
        Assert.Equal(new DateTime(2024, 1, 15), zip.InstallDate);
        Assert.Equal("You", programs[1].Scope);
    }

    [Theory]
    [InlineData("MsiExec.exe /I{1234}", "MsiExec.exe", "/I{1234}")]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /SILENT", "C:\\Program Files\\App\\unins000.exe", "/SILENT")]
    [InlineData("C:\\Apps\\uninstall.exe", "C:\\Apps\\uninstall.exe", "")]
    public void SplitsUninstallCommands(string cmd, string file, string args)
    {
        var (f, a) = InstalledProgramsService.SplitCommand(cmd);
        Assert.Equal(file, f);
        Assert.Equal(args, a);
    }

    [Fact]
    public void ParsesInstallDates()
    {
        Assert.Equal(new DateTime(2023, 3, 9), InstalledProgramsService.ParseInstallDate("20230309"));
        Assert.Equal(new DateTime(2023, 3, 9), InstalledProgramsService.ParseInstallDate("2023-03-09"));
        Assert.Null(InstalledProgramsService.ParseInstallDate("garbage"));
        Assert.Null(InstalledProgramsService.ParseInstallDate(""));
    }

    [Fact]
    public void FindsLargestFilesAndOldInstallers()
    {
        void Make(string rel, int size, int ageDays = 0)
        {
            var p = Path.Combine(_dir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllBytes(p, new byte[size]);
            File.SetLastWriteTime(p, DateTime.Now.AddDays(-ageDays));
        }
        Make("Videos/big.mp4", 5000);
        Make("Videos/bigger.mkv", 9000);
        Make("Docs/small.txt", 10);
        Make("Downloads/setup.exe", 3000, 90);
        Make("Downloads/new.msi", 3000, 1);
        Make("Downloads/photo.jpg", 3000, 90);

        var largest = LargeFileFinder.FindLargest(_dir, 2000, 2);
        Assert.Equal(["bigger.mkv", "big.mp4"], largest.Select(f => f.Name).ToArray());

        var old = LargeFileFinder.FindOldInstallers(Path.Combine(_dir, "Downloads"), TimeSpan.FromDays(30));
        Assert.Equal("setup.exe", Assert.Single(old).Name);
    }

    [Fact]
    public void ParsesScheduledTasksAndRatesServices()
    {
        var tasks = BackgroundService.ParseTasksJson("""
            [{"TaskName":"GoogleUpdateTaskMachineCore","TaskPath":"\\","State":"Ready","Author":"Google","Action":"C:\\GoogleUpdate.exe /c","Triggers":"Logon, Daily"},
             {"TaskName":"Weekly Tune-up","TaskPath":"\\WindowsTuner\\","State":"Disabled","Author":"","Action":"x","Triggers":"Weekly"}]
            """);
        Assert.Equal(2, tasks.Count);
        Assert.True(tasks[0].IsEnabled);
        Assert.Contains("Updater", tasks[0].Hint);
        Assert.False(tasks[1].IsEnabled);
        Assert.True(tasks[1].IsProtected);

        Assert.Equal(ServiceRating.Updater, BackgroundService.Rate("gupdate", "Google Update Service (gupdate)", "Google LLC"));
        Assert.Equal(ServiceRating.Updater, BackgroundService.Rate("MozillaMaintenance", "Mozilla Maintenance Service", "Mozilla"));
        Assert.Equal(ServiceRating.Keep, BackgroundService.Rate("RtkAudioUniversalService", "Realtek Audio Universal Service", "Realtek"));
        Assert.Equal(ServiceRating.Keep, BackgroundService.Rate("MBAMService", "Malwarebytes Service", "Malwarebytes"));
        Assert.Equal(ServiceRating.Optional, BackgroundService.Rate("SteamClientService", "Steam Client Service", "Valve Corporation"));
        Assert.Equal("Automatic", BackgroundService.NormalizeStartMode("Auto"));
    }

    [Fact]
    public void EvaluatesSecurityCheckup()
    {
        var checks = SecurityCheckup.Evaluate("""
            {"FirewallOff":"Public","FirewallKnown":true,"Uac":1,"Smb1":true,"SecureBoot":true,"TpmPresent":true,"TpmReady":true,
             "BitLocker":"Off","RealTime":true,"Tamper":false,"SigAge":12,"WuPausedUntil":"2099-01-01T00:00:00Z","Rdp":1,"AutoLogon":"0","Guest":false,"SmartScreen":"Warn","Hvci":false}
            """);
        CheckStatus Of(string id) => checks.Single(c => c.Id == id).Status;
        Assert.Equal(CheckStatus.Bad, Of("firewall"));
        Assert.Equal(CheckStatus.Bad, Of("smb1"));
        Assert.Equal(CheckStatus.Bad, Of("signatures"));
        Assert.Equal(CheckStatus.Good, Of("uac"));
        Assert.Equal(CheckStatus.Warning, Of("wupause"));
        Assert.Equal(CheckStatus.Warning, Of("tamper"));
        Assert.Equal(CheckStatus.Good, Of("secureboot"));
        Assert.DoesNotContain(checks, c => c.Id == "rdp");
        Assert.True(checks.Single(c => c.Id == "firewall").CanFix);
        Assert.Equal(CheckStatus.Bad, checks[0].Status); // problems first

        var off = SecurityCheckup.Evaluate("""{"Uac":0,"RealTime":false,"FirewallKnown":true,"FirewallOff":""}""");
        Assert.Equal(CheckStatus.Bad, off.Single(c => c.Id == "uac").Status);
        Assert.Equal(CheckStatus.Bad, off.Single(c => c.Id == "realtime").Status);
        Assert.Equal(CheckStatus.Good, off.Single(c => c.Id == "firewall").Status);
    }

    [Fact]
    public void ParsesEventLogReport()
    {
        var r = EventLogReport.Parse("""
            {"UnexpectedShutdowns":3,"BlueScreens":1,"DiskErrors":0,"FileSystemErrors":2,"HardwareErrors":0,"AppCrashes":14,
             "TopCrashingApps":[{"Name":"chrome.exe","Count":9},{"Name":"game.exe","Count":5}],"LastBootMs":"84250"}
            """);
        Assert.Equal(3, r.UnexpectedShutdowns);
        Assert.Equal(1, r.BlueScreens);
        Assert.Equal(84.25, r.LastBootSeconds);
        Assert.Equal("chrome.exe", r.TopCrashingApps[0].Name);
        var findings = r.Findings().ToList();
        Assert.Contains(findings, f => f.Problem && f.Text.Contains("blue screen"));
        Assert.Contains(findings, f => f.Problem && f.Text.Contains("chrome.exe ×9"));
        Assert.Contains(findings, f => f.Problem && f.Text.Contains("84 s"));

        var single = EventLogReport.Parse("""{"TopCrashingApps":{"Name":"x.exe","Count":2}}""");
        Assert.Single(single.TopCrashingApps);
        Assert.Empty(EventLogReport.Parse("").TopCrashingApps);
    }

    [Fact]
    public void ParsesBatteryReport()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012">
              <Batteries>
                <Battery>
                  <Id>DELL 7FHHV</Id><Manufacturer>SMP</Manufacturer>
                  <DesignCapacity>56000</DesignCapacity><FullChargeCapacity>36400</FullChargeCapacity><CycleCount>412</CycleCount>
                </Battery>
              </Batteries>
            </BatteryReport>
            """;
        var b = Assert.Single(BatteryReport.ParseXml(xml));
        Assert.Equal(65, Math.Round(b.HealthPercent));
        Assert.Equal(412, b.CycleCount);
        Assert.Contains("65%", b.Summary);
    }

    [Fact]
    public void SystemRunnerGuardsCleanupFolders()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        Assert.True(SystemRunner.IsSafeSystemCleanupFolder(Path.Combine(programData, "Microsoft", "Windows", "WER", "ReportQueue")));
        Assert.False(SystemRunner.IsSafeSystemCleanupFolder(programData));
        Assert.False(SystemRunner.IsSafeSystemCleanupFolder(Path.Combine(programData, "Microsoft")));      // not a whitelisted leaf
        Assert.False(SystemRunner.IsSafeSystemCleanupFolder(Path.Combine(_dir, "Temp")));                 // outside system roots
        Assert.False(SystemRunner.IsSafeSystemCleanupFolder(Path.Combine(programData, "x\" & del *", "Temp")));
        Assert.False(SystemRunner.IsSafeSystemCleanupFolder(""));

        var script = SystemRunner.BuildScript("sc.exe config x start= demand", @"C:\Windows\Temp\o.out", @"C:\Windows\Temp\o.exit");
        Assert.Contains("(sc.exe config x start= demand) > \"C:\\Windows\\Temp\\o.out\" 2>&1", script);
        Assert.Contains("echo %errorlevel%", script);
        Assert.Contains("/RU SYSTEM", SystemRunner.BuildCreateArguments("WindowsTuner\\System-1", @"C:\Windows\Temp\a.cmd"));
    }

    [Fact]
    public void NewRecommendations()
    {
        var s = new SystemSnapshot
        {
            Build = 26100, IsAdmin = true, TotalRamBytes = 16L << 30, SystemDriveTotalBytes = 500L << 30, SystemDriveFreeBytes = 200L << 30,
            BlueScreens = 2, DiskErrors = 1, SecurityProblems = 1, BootSeconds = 95, BatteryHealthPercent = 55, BiosDate = DateTime.Today.AddYears(-5),
        };
        var recs = RecommendationEngine.Evaluate(s);
        Assert.Contains(recs, r => r.Fix == FixPage.Security && r.Severity == Severity.Critical);
        Assert.Contains(recs, r => r.Title.Contains("blue screen"));
        Assert.Contains(recs, r => r.Title.Contains("disk error"));
        Assert.Contains(recs, r => r.Fix == FixPage.Background);
        Assert.Contains(recs, r => r.Title.Contains("Battery"));
        Assert.Contains(recs, r => r.Title.Contains("BIOS"));
    }

    [Fact]
    public void TuneUpIncludesOtherSoftwareByDefault()
    {
        var steps = TuneUpSteps.Create(TuneUpSteps.Defaults);
        Assert.True(steps.Single(s => s.Id == TuneUpSteps.OtherSoftware).IsSelected);
        Assert.False(steps.Single(s => s.Id == TuneUpSteps.Repair).IsSelected);
        Assert.Equal(steps.Count, steps.Select(s => s.Id).Distinct().Count());
    }

    [Fact]
    public void ExtraUpdateSourcesAreDistinct()
    {
        var sources = WindowsTuner.Core.Updates.ExtraUpdateService.CreateSources();
        Assert.Equal(sources.Count, sources.Select(s => s.Id).Distinct().Count());
        Assert.False(sources.Single(s => s.Id == "npm").DefaultSelected);
    }

    [Fact]
    public void InMemoryRegistryEnumeratesSubKeys()
    {
        var reg = new InMemoryRegistry();
        reg.SetValue(RegHive.HKLM, @"A\B\C", "v", 1, RegKind.DWord);
        reg.SetValue(RegHive.HKLM, @"A\D", "v", 1, RegKind.DWord);
        Assert.Equal(["B", "D"], reg.GetSubKeyNames(RegHive.HKLM, "A").OrderBy(x => x).ToArray());
    }
}
