using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Maintenance;
using WindowsTuner.Core.Performance;
using WindowsTuner.Core.Startup;

namespace WindowsTuner.Tests;

public class StartupAndPowerTests
{
    [Fact]
    public void StartupApprovedFlags()
    {
        Assert.True(StartupApproved.IsEnabled(null));
        Assert.True(StartupApproved.IsEnabled([0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.True(StartupApproved.IsEnabled([0x06, 0, 0, 0]));
        Assert.False(StartupApproved.IsEnabled([0x03, 0, 0, 0]));
        Assert.False(StartupApproved.IsEnabled([0x07]));

        var disabled = StartupApproved.Create(false, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        Assert.Equal(12, disabled.Length);
        Assert.Equal(0x03, disabled[0]);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).ToFileTimeUtc(), BitConverter.ToInt64(disabled, 4));
        Assert.Equal(0x02, StartupApproved.Create(true)[0]);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Steam\\steam.exe\" -silent", "C:\\Program Files\\Steam\\steam.exe")]
    [InlineData("C:\\Program Files\\App Dir\\app.exe /tray", "C:\\Program Files\\App Dir\\app.exe")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL", "rundll32.exe")]
    [InlineData("\"C:\\x.exe\"", "C:\\x.exe")]
    [InlineData("", "")]
    public void ExtractsExecutable(string command, string expected) =>
        Assert.Equal(expected, StartupApproved.ExtractExecutable(command));

    [Fact]
    public void ParsesPowercfgListInAnyLanguage()
    {
        const string output = """

            Existing Power Schemes (* Active)
            -----------------------------------
            Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
            Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
            GUID du mode de gestion de l'alimentation : a1841308-3541-4fab-bc81-f71556f20b4a  (Économies d'énergie)
            """;
        var plans = PowerPlanService.ParseList(output);
        Assert.Equal(3, plans.Count);
        Assert.True(plans[0].IsActive);
        Assert.Equal(PowerPlanService.Balanced, plans[0].Guid);
        Assert.Equal("High performance", plans[1].Name);
        Assert.False(plans[1].IsActive);
        Assert.Equal("Économies d'énergie", plans[2].Name);
    }

    [Fact]
    public void SchedulerArguments()
    {
        var args = Scheduler.BuildCreateArguments(@"C:\Tools\WindowsTuner.exe", DayOfWeek.Sunday, 9);
        Assert.Contains("/SC WEEKLY /D SUN /ST 09:00", args);
        Assert.Contains("/RL HIGHEST", args);
        Assert.Contains(@"\""C:\Tools\WindowsTuner.exe\"" --tune-up", args);
    }

    [Fact]
    public void FormatsBytes()
    {
        Assert.Equal("0 B", Format.Bytes(0));
        Assert.Equal("1.00 KB", Format.Bytes(1024));
        Assert.Equal("1.50 GB", Format.Bytes(1610612736));
        Assert.Equal("120 MB", Format.Bytes(120L * 1024 * 1024));
    }

    [Fact]
    public void CleansConsoleLines()
    {
        Assert.Equal("Done", ProcessRunner.CleanConsoleLine("50%\r100%\rDone"));
        Assert.Equal("ab", ProcessRunner.CleanConsoleLine("abc\b"));
        Assert.True(ProcessRunner.IsProgressNoise("-"));
        Assert.True(ProcessRunner.IsProgressNoise("██████▒▒▒▒  1.2 MB / 3 MB"));
        Assert.False(ProcessRunner.IsProgressNoise("Successfully installed"));
    }
}
