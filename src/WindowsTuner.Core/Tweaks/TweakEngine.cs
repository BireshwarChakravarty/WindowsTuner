using System.Globalization;
using System.Reflection;
using System.Text.Json;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Tweaks;

/// <summary>
/// Applies declarative registry/command tweaks. The original value of everything it touches is written to an
/// undo journal the first time a tweak is turned on, so "off" restores exactly what was there before.
/// </summary>
public sealed class TweakEngine
{
    public delegate Task<ProcessResult> CommandRunner(string command, CancellationToken ct);

    private readonly IRegistryAccess _registry;
    private readonly CommandRunner _runCommand;
    private readonly string _journalPath;
    private Dictionary<string, List<JournalValue>> _journal;

    public TweakEngine(IRegistryAccess registry, CommandRunner? runCommand = null, string? journalPath = null)
    {
        _registry = registry;
        _runCommand = runCommand ?? DefaultRunner;
        _journalPath = journalPath ?? AppPaths.TweakJournalFile;
        _journal = JsonFile.Read<Dictionary<string, List<JournalValue>>>(_journalPath)
                   ?? new Dictionary<string, List<JournalValue>>(StringComparer.OrdinalIgnoreCase);
        _journal = new Dictionary<string, List<JournalValue>>(_journal, StringComparer.OrdinalIgnoreCase);
    }

    private static Task<ProcessResult> DefaultRunner(string command, CancellationToken ct) =>
        ProcessRunner.RunAsync("cmd.exe", "/d /c " + command, Log.Output, ct: ct);

    public IReadOnlyCollection<string> JournaledTweakIds => _journal.Keys;

    #region Catalog

    public static IReadOnlyList<TweakDefinition> LoadBuiltInCatalog()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WindowsTuner.Core.tweaks.json")
                           ?? throw new InvalidOperationException("Built-in tweak catalog is missing.");
        using var reader = new StreamReader(stream);
        return ParseCatalog(reader.ReadToEnd());
    }

    public static IReadOnlyList<TweakDefinition> ParseCatalog(string json, bool isCustom = false)
    {
        var file = JsonSerializer.Deserialize<TweakCatalogFile>(json, JsonFile.Options) ?? new TweakCatalogFile();
        foreach (var t in file.Tweaks)
        {
            if (string.IsNullOrWhiteSpace(t.Id)) throw new FormatException($"Tweak '{t.Name}' has no id.");
            if (t.Registry.Count == 0 && t.OnCommands.Count == 0)
                throw new FormatException($"Tweak '{t.Id}' does nothing (no registry entries or commands).");
            foreach (var r in t.Registry)
            {
                _ = r.ParsedHive;
                if (r.On is null) throw new FormatException($"Tweak '{t.Id}': every registry entry needs an 'on' value.");
                if (string.IsNullOrWhiteSpace(r.Path)) throw new FormatException($"Tweak '{t.Id}': registry entry without a path.");
            }
            t.IsCustom = isCustom;
        }
        var dup = file.Tweaks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new FormatException($"Duplicate tweak id '{dup.Key}'.");
        return file.Tweaks;
    }

    /// <summary>Built-in catalog plus the user's custom-tweaks.json (custom entries override built-ins with the same id).</summary>
    public static IReadOnlyList<TweakDefinition> LoadCatalog(string? customPath = null)
    {
        var all = LoadBuiltInCatalog().ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        customPath ??= AppPaths.CustomTweaksFile;
        if (File.Exists(customPath))
        {
            try
            {
                foreach (var t in ParseCatalog(File.ReadAllText(customPath), isCustom: true)) all[t.Id] = t;
                Log.Info($"Loaded custom tweaks from {customPath}");
            }
            catch (Exception ex)
            {
                Log.Error($"custom-tweaks.json ignored: {ex.Message}");
            }
        }
        return all.Values.ToList();
    }

    #endregion

    #region State

    public TweakState GetState(TweakDefinition t, int osBuild)
    {
        if (!t.IsApplicable(osBuild)) return TweakState.NotApplicable;
        if (t.Registry.Count == 0) return TweakState.Off; // command-only tweak: unknown, show as off
        var matches = t.Registry.Count(r => ValuesEqual(_registry.GetValue(r.ParsedHive, r.Path, r.Name), ToRegistryValue(r.On, r.Kind)));
        return matches == t.Registry.Count ? TweakState.On : matches == 0 ? TweakState.Off : TweakState.Partial;
    }

    public void Refresh(TweakItem item, int osBuild) => item.State = GetState(item.Definition, osBuild);

    #endregion

    #region Apply / revert

    public async Task ApplyAsync(TweakDefinition t, bool on, CancellationToken ct = default)
    {
        if (on) await TurnOnAsync(t, ct);
        else await TurnOffAsync(t, ct);
    }

    private async Task TurnOnAsync(TweakDefinition t, CancellationToken ct)
    {
        if (!_journal.ContainsKey(t.Id))
        {
            _journal[t.Id] = t.Registry.Where(r => !r.DetectOnly).Select(Capture).ToList();
            SaveJournal();
        }

        foreach (var r in t.Registry.Where(r => !r.DetectOnly))
            Write(r, r.On);

        foreach (var cmd in t.OnCommands) await RunAsync(t, cmd, ct);
        Log.Success($"Tweak on: {t.Name}");
    }

    private async Task TurnOffAsync(TweakDefinition t, CancellationToken ct)
    {
        if (_journal.TryGetValue(t.Id, out var originals))
        {
            foreach (var o in originals) Restore(o);
        }
        else
        {
            foreach (var r in t.Registry.Where(r => !r.DetectOnly))
            {
                if (r.DeleteKeyWhenOff) _registry.DeleteKey(r.ParsedHive, r.Path);
                else Write(r, r.Off);
            }
        }

        foreach (var cmd in t.OffCommands) await RunAsync(t, cmd, ct);
        if (_journal.Remove(t.Id)) SaveJournal();
        Log.Success($"Tweak off: {t.Name}");
    }

    private async Task RunAsync(TweakDefinition t, string command, CancellationToken ct)
    {
        var r = await _runCommand(command, ct);
        if (!r.Success) Log.Warn($"{t.Name}: '{command}' exited with {r.ExitCode}");
    }

    private void Write(RegistryChange r, JsonElement? value)
    {
        var data = ToRegistryValue(value, r.Kind);
        if (data is null) _registry.DeleteValue(r.ParsedHive, r.Path, r.Name);
        else _registry.SetValue(r.ParsedHive, r.Path, r.Name, data, r.Kind);
    }

    private JournalValue Capture(RegistryChange r)
    {
        var hive = r.ParsedHive;
        var value = _registry.GetValue(hive, r.Path, r.Name);
        var kind = _registry.GetKind(hive, r.Path, r.Name) ?? r.Kind;
        return new JournalValue
        {
            Hive = r.Hive,
            Path = r.Path,
            Name = r.Name,
            KeyExisted = _registry.KeyExists(hive, r.Path),
            DeleteKeyIfAbsent = r.DeleteKeyWhenOff,
            Existed = value is not null,
            Kind = kind,
            Data = value is null ? null : Serialize(value),
        };
    }

    private void Restore(JournalValue o)
    {
        var hive = RegHiveParser.Parse(o.Hive);
        if (!o.KeyExisted && o.DeleteKeyIfAbsent)
        {
            _registry.DeleteKey(hive, o.Path);
            return;
        }
        if (o.Existed && o.Data is not null)
            _registry.SetValue(hive, o.Path, o.Name, Deserialize(o.Data, o.Kind), o.Kind);
        else
            _registry.DeleteValue(hive, o.Path, o.Name);
    }

    /// <summary>Turns off every tweak WindowsTuner has turned on (restoring original values).</summary>
    public async Task<int> RevertAllAsync(IEnumerable<TweakDefinition> catalog, CancellationToken ct = default)
    {
        var byId = catalog.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var id in _journal.Keys.ToList())
        {
            if (byId.TryGetValue(id, out var t))
                await TurnOffAsync(t, ct);
            else
            {
                foreach (var o in _journal[id]) Restore(o);
                _journal.Remove(id);
                SaveJournal();
            }
            count++;
        }
        return count;
    }

    private void SaveJournal()
    {
        try { JsonFile.Write(_journalPath, _journal); }
        catch (Exception ex) { Log.Warn("Could not save tweak journal: " + ex.Message); }
    }

    #endregion

    #region Profiles

    public TweakProfile CreateProfile(IEnumerable<TweakItem> items, string name) => new()
    {
        Name = name,
        Computer = Environment.MachineName,
        Tweaks = items.Where(i => i.IsApplicable && i.State != TweakState.Partial)
            .ToDictionary(i => i.Id, i => i.IsOn, StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>Applies a saved profile; returns how many tweaks changed.</summary>
    public async Task<int> ApplyProfileAsync(TweakProfile profile, IEnumerable<TweakItem> items, int osBuild, CancellationToken ct = default)
    {
        var changed = 0;
        foreach (var item in items)
        {
            if (!profile.Tweaks.TryGetValue(item.Id, out var wantOn) || !item.IsApplicable) continue;
            if (item.IsOn == wantOn) continue;
            await ApplyAsync(item.Definition, wantOn, ct);
            Refresh(item, osBuild);
            changed++;
        }
        return changed;
    }

    #endregion

    #region Value conversion

    public static object? ToRegistryValue(JsonElement? element, RegKind kind)
    {
        if (element is not { } e || e.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        return kind switch
        {
            RegKind.DWord => unchecked((int)(e.ValueKind == JsonValueKind.Number
                ? (e.TryGetInt64(out var l) ? l : (long)e.GetUInt64())
                : long.Parse(e.GetString()!, CultureInfo.InvariantCulture))),
            RegKind.QWord => e.ValueKind == JsonValueKind.Number ? e.GetInt64() : long.Parse(e.GetString()!, CultureInfo.InvariantCulture),
            RegKind.Binary => ParseHex(e.GetString() ?? ""),
            RegKind.MultiString => e.ValueKind == JsonValueKind.Array
                ? e.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : (e.GetString() ?? "").Split('\n'),
            _ => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString(),
        };
    }

    public static bool ValuesEqual(object? current, object? desired)
    {
        if (current is null || desired is null) return current is null && desired is null;
        switch (desired)
        {
            case int d:
                return current switch
                {
                    int ci => ci == d,
                    long cl => cl == d,
                    string cs => long.TryParse(cs, out var p) && p == d,
                    _ => false,
                };
            case long d:
                return current switch { long cl => cl == d, int ci => ci == d, _ => false };
            case byte[] d:
                return current is byte[] cb && cb.AsSpan().SequenceEqual(d);
            case string[] d:
                return current is string[] ca && ca.SequenceEqual(d);
            default:
                return string.Equals(current.ToString(), desired.ToString(), StringComparison.Ordinal);
        }
    }

    private static byte[] ParseHex(string hex)
    {
        var clean = new string(hex.Where(Uri.IsHexDigit).ToArray());
        return Convert.FromHexString(clean);
    }

    private static string Serialize(object value) => value switch
    {
        byte[] b => Convert.ToHexString(b),
        string[] a => string.Join('\n', a),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static object Deserialize(string data, RegKind kind) => kind switch
    {
        RegKind.DWord => int.Parse(data, CultureInfo.InvariantCulture),
        RegKind.QWord => long.Parse(data, CultureInfo.InvariantCulture),
        RegKind.Binary => Convert.FromHexString(data),
        RegKind.MultiString => data.Split('\n'),
        _ => data,
    };

    #endregion

    public sealed class JournalValue
    {
        public string Hive { get; set; } = "";
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public bool KeyExisted { get; set; }
        public bool DeleteKeyIfAbsent { get; set; }
        public bool Existed { get; set; }
        public RegKind Kind { get; set; }
        public string? Data { get; set; }
    }

    /// <summary>Restarts Windows Explorer so taskbar/Explorer tweaks take effect immediately.</summary>
    public static async Task RestartExplorerAsync()
    {
        Log.Info("Restarting Windows Explorer…");
        await ProcessRunner.RunAsync("taskkill.exe", "/f /im explorer.exe");
        await Task.Delay(2500);
        // Windows normally relaunches the shell itself; start it if it didn't.
        if (System.Diagnostics.Process.GetProcessesByName("explorer").Length == 0)
            Shell.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "");
    }
}
