using WindowsTuner.Core.Apps;
using WindowsTuner.Core.Drivers;
using WindowsTuner.Core.Health;
using WindowsTuner.Core.SystemInfo;

namespace WindowsTuner.Tests;

public class RecommendationTests
{
    private static SystemSnapshot Healthy() => new()
    {
        Build = 26100, IsAdmin = true, TotalRamBytes = 16L << 30, MemoryLoadPercent = 40,
        SystemDriveTotalBytes = 500L << 30, SystemDriveFreeBytes = 200L << 30, SystemDriveMedia = "SSD",
        Uptime = TimeSpan.FromHours(5), EnabledStartupApps = 4, AppUpdatesAvailable = 0, WindowsUpdatesAvailable = 0,
        DefenderRealTimeOn = true, DefenderSignatureAgeDays = 0,
    };

    [Fact]
    public void HealthyPcIsGood()
    {
        var recs = RecommendationEngine.Evaluate(Healthy());
        Assert.Equal(Severity.Good, Assert.Single(recs).Severity);
        Assert.Equal(100, RecommendationEngine.Score(recs));
    }

    [Fact]
    public void LongNeglectedPcGetsPrioritisedAdvice()
    {
        var s = Healthy();
        s.Build = 19045;
        s.Uptime = TimeSpan.FromDays(40);
        s.FastStartupEnabled = true;
        s.PendingReboot = true;
        s.SystemDriveFreeBytes = 8L << 30;
        s.SystemDriveMedia = "HDD";
        s.EnabledStartupApps = 17;
        s.AppUpdatesAvailable = 23;
        s.DefenderSignatureAgeDays = 120;

        var recs = RecommendationEngine.Evaluate(s);
        Assert.Equal(Severity.Critical, recs[0].Severity);
        Assert.Contains(recs, r => r.Title.Contains("Windows 10"));
        Assert.Contains(recs, r => r.Detail.Contains("Fast Startup"));
        Assert.Contains(recs, r => r.Fix == FixPage.Cleanup && r.Severity == Severity.Critical);
        Assert.Contains(recs, r => r.Title.Contains("hard disk"));
        Assert.Contains(recs, r => r.Fix == FixPage.Startup);
        Assert.Contains(recs, r => r.Title.Contains("23 app update"));
        Assert.True(RecommendationEngine.Score(recs) < 30);
        // Sorted most severe first.
        Assert.True(recs.Zip(recs.Skip(1)).All(p => p.First.Severity >= p.Second.Severity));
    }

    [Fact]
    public void Old24H2Windows11Warns()
    {
        var s = Healthy();
        s.Build = 22631;
        Assert.Contains(RecommendationEngine.Evaluate(s), r => r.Fix == FixPage.Updates && r.Severity == Severity.Warning);
    }

    [Fact]
    public void DriverAgeIgnoresInboxPlaceholderDate()
    {
        var inbox = new DriverInfo("Generic USB Hub", "USB", "Microsoft", "10.0.1", new DateTime(2006, 6, 21), "usb.inf", true);
        var old = new DriverInfo("Realtek Audio", "MEDIA", "Realtek", "6.0.1", DateTime.Today.AddYears(-6), "oem12.inf", true);
        Assert.False(inbox.LooksOutdated());
        Assert.True(old.LooksOutdated());
        Assert.Equal(new DateTime(2023, 6, 15), DriverInfo.ParseCimDate("20230615000000.******+000"));
        Assert.Null(DriverInfo.ParseCimDate("garbage"));
    }

    [Fact]
    public void VendorAdvisorPicksOfficialTools()
    {
        var tools = VendorToolAdvisor.Recommend(["NVIDIA GeForce RTX 4070", "Intel(R) UHD Graphics 770"], "Dell Inc.", "13th Gen Intel(R) Core(TM) i7");
        Assert.Contains(tools, t => t.Name.StartsWith("NVIDIA"));
        Assert.Contains(tools, t => t.Name.StartsWith("Intel"));
        Assert.Contains(tools, t => t.Name.StartsWith("Dell"));
        Assert.All(tools, t => Assert.StartsWith("https://", t.Url));

        var custom = VendorToolAdvisor.Recommend(["AMD Radeon RX 7800 XT"], "System manufacturer", "AMD Ryzen 7 7800X3D");
        Assert.Contains(custom, t => t.Name.Contains("Chipset"));
        Assert.Contains(custom, t => t.Name.Contains("Motherboard"));
    }

    [Fact]
    public void ProblemDeviceDescriptions()
    {
        Assert.Contains("Code 43", ProblemDevice.DescribeError(43));
        Assert.Contains("Code 28", ProblemDevice.DescribeError(28));
    }

    [Fact]
    public void BloatRatings()
    {
        Assert.Equal(BloatRating.Suggested, BloatCatalog.Rate("Microsoft.BingNews"));
        Assert.Equal(BloatRating.Suggested, BloatCatalog.Rate("king.com.CandyCrushSaga"));
        Assert.Equal(BloatRating.Optional, BloatCatalog.Rate("Microsoft.YourPhone"));
        Assert.True(BloatCatalog.IsProtected("Microsoft.WindowsStore"));
        Assert.True(BloatCatalog.IsProtected("Microsoft.DesktopAppInstaller"));
        Assert.True(BloatCatalog.IsProtected("Microsoft.VCLibs.140.00.UWPDesktop"));
        var pkg = new AppxPackageInfo { Name = "Microsoft.SecHealthUI", PackageFullName = "x" };
        pkg.IsSelected = true;
        Assert.False(pkg.IsSelected);
    }

    [Fact]
    public void ParsesAppxAndDiskJson()
    {
        var apps = AppxPackageInfo.ParseJson("""[{"Name":"Microsoft.BingNews","PackageFullName":"Microsoft.BingNews_4.1_x64__8wekyb3d8bbwe","Version":"4.1","Publisher":"CN=Microsoft"}]""");
        Assert.Equal("Microsoft News", Assert.Single(apps).FriendlyName);

        var disks = DiskHealth.ParseJson("""[{"FriendlyName":"Samsung SSD 980","MediaType":"4","BusType":"17","HealthStatus":"0","Size":1000204886016,"Wear":3,"Temperature":41,"ReadErrorsUncorrected":0},{"FriendlyName":"WDC WD10","MediaType":"HDD","BusType":"SATA","HealthStatus":"Warning","Size":1000,"Wear":null,"Temperature":null,"ReadErrorsUncorrected":null}]""");
        Assert.Equal(2, disks.Count);
        Assert.Equal("SSD", disks[0].MediaType);
        Assert.Equal("NVMe", disks[0].BusType);
        Assert.True(disks[0].IsHealthy);
        Assert.True(disks[1].IsHdd);
        Assert.False(disks[1].IsHealthy);
        Assert.Equal("—", disks[1].Wear);
    }
}
