using WindowsTuner.Core.Infrastructure;
using WindowsTuner.Core.Tweaks;

namespace WindowsTuner.Tests;

public class TweakEngineTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("wt-tests").FullName;
    private readonly InMemoryRegistry _reg = new();
    private readonly List<string> _commands = [];

    public TweakEngineTests() => Log.FileLoggingEnabled = false;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private TweakEngine Engine() => new(_reg, (cmd, _) =>
    {
        _commands.Add(cmd);
        return Task.FromResult(new ProcessResult(0, "", ""));
    }, Path.Combine(_dir, "journal.json"));

    private const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    [Fact]
    public void BuiltInCatalogIsValid()
    {
        var catalog = TweakEngine.LoadBuiltInCatalog();
        Assert.True(catalog.Count >= 40);
        Assert.All(catalog, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
            Assert.Contains('.', t.Id);
        });
        Assert.Equal(catalog.Count, catalog.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void EveryCatalogValueConvertsForItsKind()
    {
        foreach (var t in TweakEngine.LoadBuiltInCatalog())
        foreach (var r in t.Registry)
        {
            Assert.NotNull(TweakEngine.ToRegistryValue(r.On, r.Kind));
            _ = TweakEngine.ToRegistryValue(r.Off, r.Kind); // may be null (= delete)
            _ = r.ParsedHive;
        }
    }

    [Fact]
    public async Task TurnOnThenOffRestoresOriginalValue()
    {
        _reg.SetValue(RegHive.HKCU, Adv, "HideFileExt", 1, RegKind.DWord);
        var t = TweakEngine.LoadBuiltInCatalog().Single(x => x.Id == "explorer.showExtensions");
        var engine = Engine();

        Assert.Equal(TweakState.Off, engine.GetState(t, 26100));
        await engine.ApplyAsync(t, on: true);
        Assert.Equal(0, _reg.GetValue(RegHive.HKCU, Adv, "HideFileExt"));
        Assert.Equal(TweakState.On, engine.GetState(t, 26100));

        await engine.ApplyAsync(t, on: false);
        Assert.Equal(1, _reg.GetValue(RegHive.HKCU, Adv, "HideFileExt"));
        Assert.Empty(engine.JournaledTweakIds);
    }

    [Fact]
    public async Task OriginallyMissingValueIsDeletedOnRevert()
    {
        var t = TweakEngine.LoadBuiltInCatalog().Single(x => x.Id == "explorer.openThisPC");
        var engine = Engine();
        await engine.ApplyAsync(t, true);
        Assert.Equal(1, _reg.GetValue(RegHive.HKCU, Adv, "LaunchTo"));
        await engine.ApplyAsync(t, false);
        Assert.Null(_reg.GetValue(RegHive.HKCU, Adv, "LaunchTo"));
    }

    [Fact]
    public async Task JournalSurvivesNewEngineInstance()
    {
        _reg.SetValue(RegHive.HKCU, @"Control Panel\Desktop", "MenuShowDelay", "250", RegKind.String);
        var t = TweakEngine.LoadBuiltInCatalog().Single(x => x.Id == "perf.fastMenus");
        await Engine().ApplyAsync(t, true);
        Assert.Equal("100", _reg.GetValue(RegHive.HKCU, @"Control Panel\Desktop", "MenuShowDelay"));

        // Simulate an app restart: the original "250" (not the default "400") must come back.
        await Engine().ApplyAsync(t, false);
        Assert.Equal("250", _reg.GetValue(RegHive.HKCU, @"Control Panel\Desktop", "MenuShowDelay"));
    }

    [Fact]
    public async Task ClassicContextMenuDeletesKeyOnRevert()
    {
        var t = TweakEngine.LoadBuiltInCatalog().Single(x => x.Id == "explorer.classicContextMenu");
        var engine = Engine();
        const string key = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
        await engine.ApplyAsync(t, true);
        Assert.Equal("", _reg.GetValue(RegHive.HKCU, key, ""));
        Assert.Equal(TweakState.On, engine.GetState(t, 22631));
        await engine.ApplyAsync(t, false);
        Assert.False(_reg.KeyExists(RegHive.HKCU, key));
    }

    [Fact]
    public async Task WithoutJournalTurnOffUsesOffValues()
    {
        _reg.SetValue(RegHive.HKCU, Adv, "TaskbarAl", 0, RegKind.DWord); // user already had it left-aligned
        var t = TweakEngine.LoadBuiltInCatalog().Single(x => x.Id == "taskbar.alignLeft");
        var engine = Engine();
        Assert.Equal(TweakState.On, engine.GetState(t, 22631));
        await engine.ApplyAsync(t, false);
        Assert.Equal(1, _reg.GetValue(RegHive.HKCU, Adv, "TaskbarAl"));
    }

    [Fact]
    public void BuildRangeMakesTweakNotApplicable()
    {
        var t = TweakEngine.LoadBuiltInCatalog().Single(x => x.Id == "taskbar.alignLeft");
        Assert.Equal(TweakState.NotApplicable, Engine().GetState(t, 19045)); // Windows 10
    }

    [Fact]
    public async Task PartialStateAndCommands()
    {
        var t = TweakEngine.LoadBuiltInCatalog().Single(x => x.Id == "svc.noSysMain");
        var engine = Engine();
        await engine.ApplyAsync(t, true);
        Assert.Contains("sc.exe stop SysMain", _commands);
        Assert.Equal(TweakState.On, engine.GetState(t, 26100));

        var multi = TweakEngine.LoadBuiltInCatalog().Single(x => x.Id == "explorer.noRecentFiles");
        _reg.SetValue(RegHive.HKCU, @"Software\Microsoft\Windows\CurrentVersion\Explorer", "ShowRecent", 0, RegKind.DWord);
        Assert.Equal(TweakState.Partial, engine.GetState(multi, 26100));
    }

    [Fact]
    public async Task DetectOnlyEntriesAreNotWritten()
    {
        var t = TweakEngine.LoadBuiltInCatalog().Single(x => x.Id == "perf.hibernateOff");
        await Engine().ApplyAsync(t, true);
        Assert.Null(_reg.GetValue(RegHive.HKLM, @"SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled"));
        Assert.Contains("powercfg.exe /hibernate off", _commands);
    }

    [Fact]
    public async Task RevertAllRestoresEverything()
    {
        var catalog = TweakEngine.LoadBuiltInCatalog();
        var engine = Engine();
        foreach (var id in new[] { "explorer.showExtensions", "privacy.adId", "perf.fastMenus" })
            await engine.ApplyAsync(catalog.Single(t => t.Id == id), true);
        Assert.Equal(3, engine.JournaledTweakIds.Count);
        Assert.Equal(3, await engine.RevertAllAsync(catalog));
        Assert.Empty(engine.JournaledTweakIds);
        Assert.Null(_reg.GetValue(RegHive.HKCU, Adv, "HideFileExt"));
    }

    [Fact]
    public async Task ProfilesRoundTrip()
    {
        var catalog = TweakEngine.LoadBuiltInCatalog();
        var engine = Engine();
        var items = catalog.Select(d => new TweakItem(d)).ToList();
        foreach (var i in items) engine.Refresh(i, 26100);

        var profile = new TweakProfile { Tweaks = { ["explorer.showExtensions"] = true, ["privacy.adId"] = true } };
        Assert.Equal(2, await engine.ApplyProfileAsync(profile, items, 26100));
        Assert.True(items.Single(i => i.Id == "privacy.adId").IsOn);
        Assert.Equal(0, await engine.ApplyProfileAsync(profile, items, 26100));

        var exported = engine.CreateProfile(items, "x");
        Assert.True(exported.Tweaks["explorer.showExtensions"]);
    }

    [Fact]
    public void CustomCatalogValidation()
    {
        Assert.Throws<FormatException>(() => TweakEngine.ParseCatalog("""{ "tweaks": [ { "id": "a.b", "name": "x" } ] }"""));
        Assert.Throws<FormatException>(() => TweakEngine.ParseCatalog("""{ "tweaks": [ { "id": "a.b", "name": "x", "registry": [ { "hive": "HKXX", "path": "p", "name": "n", "on": 1 } ] } ] }"""));
        var ok = TweakEngine.ParseCatalog("""
            // comments are allowed
            { "tweaks": [ { "id": "my.tweak", "name": "Mine", "category": "Custom",
              "registry": [ { "hive": "HKCU", "path": "Software\\Test", "name": "V", "kind": "Binary", "on": "02 00 FF" } ] } ] }
            """, isCustom: true);
        Assert.True(ok.Single().IsCustom);
        Assert.Equal(new byte[] { 2, 0, 255 }, TweakEngine.ToRegistryValue(ok[0].Registry[0].On, RegKind.Binary));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1L, 1, true)]
    [InlineData("1", 1, true)]
    [InlineData(2, 1, false)]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "ABC", false)]
    public void ValueComparison(object current, object desired, bool expected) =>
        Assert.Equal(expected, TweakEngine.ValuesEqual(current, desired));
}
