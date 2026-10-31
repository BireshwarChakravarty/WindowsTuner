using WindowsTuner.Core.Updates;

namespace WindowsTuner.Tests;

public class WingetParserTests
{
    private const string Typical = """
           -    \    |    /
        Name                               Id                           Version        Available      Source
        -----------------------------------------------------------------------------------------------------
        Google Chrome                      Google.Chrome                129.0.6668.90  130.0.6723.70  winget
        Microsoft Visual C++ 2015-2022 Re… Microsoft.VCRedist.2015+.x64 14.38.33135.0  14.40.33810.0  winget
        7-Zip 23.01 (x64)                  7zip.7zip                    23.01          24.08          winget
        Discord                            Discord.Discord              Unknown        1.0.9163       winget
        Zoom Workplace                     Zoom.Zoom                    < 6.2.0        6.2.5.48698    winget
        5 upgrades available.

        The following packages have an upgrade available, but require explicit targeting for upgrade:
        Name    Id           Version Available Source
        ---------------------------------------------
        Spotify Spotify.Spotify 1.2.3 1.2.50 winget
        """;

    [Fact]
    public void ParsesRowsRightToLeft()
    {
        var rows = WingetParser.ParseUpgradeTable(Typical);
        Assert.Equal(6, rows.Count);

        var chrome = rows[0];
        Assert.Equal("Google Chrome", chrome.Name);
        Assert.Equal("Google.Chrome", chrome.Id);
        Assert.Equal("129.0.6668.90", chrome.InstalledVersion);
        Assert.Equal("130.0.6723.70", chrome.AvailableVersion);
        Assert.Equal("winget", chrome.Source);
        Assert.False(chrome.RequiresExplicitTarget);
    }

    [Fact]
    public void HandlesNamesWithSpacesAndEllipsis()
    {
        var rows = WingetParser.ParseUpgradeTable(Typical);
        Assert.Contains(rows, r => r.Id == "Microsoft.VCRedist.2015+.x64" && r.Name.StartsWith("Microsoft Visual C++"));
        Assert.Contains(rows, r => r.Id == "7zip.7zip" && r.Name == "7-Zip 23.01 (x64)");
    }

    [Fact]
    public void HandlesUnknownAndRangeVersions()
    {
        var rows = WingetParser.ParseUpgradeTable(Typical);
        var discord = rows.Single(r => r.Id == "Discord.Discord");
        Assert.True(discord.IsVersionUnknown);
        var zoom = rows.Single(r => r.Id == "Zoom.Zoom");
        Assert.Equal("< 6.2.0", zoom.InstalledVersion);
        Assert.True(zoom.IsVersionUnknown);
    }

    [Fact]
    public void MarksExplicitTargetSection()
    {
        var rows = WingetParser.ParseUpgradeTable(Typical);
        Assert.True(rows.Single(r => r.Id == "Spotify.Spotify").RequiresExplicitTarget);
    }

    [Fact]
    public void WorksWithLocalizedHeadersAndWideCharacters()
    {
        const string german = """
            Name             ID                Version  Verfügbar  Quelle
            ------------------------------------------------------------
            微信              Tencent.WeChat    3.9.0    3.9.12     winget
            Notepad++ (64-bit x64) Notepad++.Notepad++ 8.6 8.7.1 winget
            2 Aktualisierungen verfügbar.
            """;
        var rows = WingetParser.ParseUpgradeTable(german);
        Assert.Equal(2, rows.Count);
        Assert.Equal("微信", rows[0].Name);
        Assert.Equal("Tencent.WeChat", rows[0].Id);
        Assert.Equal("Notepad++.Notepad++", rows[1].Id);
        Assert.Equal("8.7.1", rows[1].AvailableVersion);
    }

    [Fact]
    public void EmptyWhenNothingToUpgrade()
    {
        Assert.Empty(WingetParser.ParseUpgradeTable("No installed package found matching input criteria.\r\n"));
        Assert.Empty(WingetParser.ParseUpgradeTable(""));
    }

    [Fact]
    public void HandlesCarriageReturnProgressNoise()
    {
        var output = "  -\r  \\\r  |\rName  Id  Version  Available  Source\r\n" +
                     "----------------------------------------\r\n" +
                     "VLC media player  VideoLAN.VLC  3.0.20  3.0.21  winget\r\n";
        var rows = WingetParser.ParseUpgradeTable(output);
        Assert.Single(rows);
        Assert.Equal("VideoLAN.VLC", rows[0].Id);
    }

    [Fact]
    public void DetectsTruncatedIds()
    {
        const string output = """
            Name  Id  Version  Available  Source
            ------------------------------------
            Some App  Contoso.SomeVeryLongPackageIdent… 1.0 2.0 winget
            """;
        var row = Assert.Single(WingetParser.ParseUpgradeTable(output));
        Assert.True(row.IsIdTruncated);
    }

    [Fact]
    public void ParsesModuleJsonArrayAndSingleObject()
    {
        var arr = WingetParser.ParseModuleJson("""[{"Name":"Git","Id":"Git.Git","Version":"2.44.0","Available":"2.47.0","Source":"winget"},{"Name":"Node","Id":"OpenJS.NodeJS.LTS","Version":null,"Available":"22.11.0","Source":"winget"}]""");
        Assert.Equal(2, arr.Count);
        Assert.Equal("Unknown", arr[1].InstalledVersion);

        var single = WingetParser.ParseModuleJson("""{"Name":"Git","Id":"Git.Git","Version":"2.44.0","Available":"2.47.0","Source":"winget"}""");
        Assert.Equal("Git.Git", Assert.Single(single).Id);
        Assert.Empty(WingetParser.ParseModuleJson("[]"));
    }

    [Fact]
    public void DescribesKnownExitCodes()
    {
        Assert.Contains("currently running", WingetParser.DescribeExitCode(unchecked((int)0x8A150101)));
        Assert.Contains("0x8A15FFFF", WingetParser.DescribeExitCode(unchecked((int)0x8A15FFFF)));
    }
}
