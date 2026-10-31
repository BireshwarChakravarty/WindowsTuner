using Microsoft.Win32;

namespace WindowsTuner.Core.Infrastructure;

public enum RegHive { HKCU, HKLM, HKU, HKCR }

public enum RegKind { String, ExpandString, DWord, QWord, Binary, MultiString }

/// <summary>Registry abstraction so the tweak engine and startup manager can be tested without Windows.</summary>
public interface IRegistryAccess
{
    /// <summary>Returns the value (int, long, string, byte[], string[]) or null when absent.</summary>
    object? GetValue(RegHive hive, string path, string name);
    RegKind? GetKind(RegHive hive, string path, string name);
    void SetValue(RegHive hive, string path, string name, object value, RegKind kind);
    void DeleteValue(RegHive hive, string path, string name);
    void DeleteKey(RegHive hive, string path);
    bool KeyExists(RegHive hive, string path);
    IReadOnlyList<string> GetValueNames(RegHive hive, string path);
}

public static class RegHiveParser
{
    public static RegHive Parse(string text) => text.Trim().ToUpperInvariant() switch
    {
        "HKCU" or "HKEY_CURRENT_USER" => RegHive.HKCU,
        "HKLM" or "HKEY_LOCAL_MACHINE" => RegHive.HKLM,
        "HKU" or "HKEY_USERS" => RegHive.HKU,
        "HKCR" or "HKEY_CLASSES_ROOT" => RegHive.HKCR,
        _ => throw new FormatException($"Unknown registry hive '{text}'"),
    };
}

public sealed class WindowsRegistry : IRegistryAccess
{
    public static WindowsRegistry Instance { get; } = new();

    private static RegistryKey Base(RegHive hive)
    {
        var view = Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default;
        return hive switch
        {
            RegHive.HKCU => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view),
            RegHive.HKLM => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view),
            RegHive.HKU => RegistryKey.OpenBaseKey(RegistryHive.Users, view),
            RegHive.HKCR => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view),
            _ => throw new ArgumentOutOfRangeException(nameof(hive)),
        };
    }

    public object? GetValue(RegHive hive, string path, string name)
    {
        using var root = Base(hive);
        using var key = root.OpenSubKey(path, writable: false);
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    public RegKind? GetKind(RegHive hive, string path, string name)
    {
        using var root = Base(hive);
        using var key = root.OpenSubKey(path, writable: false);
        if (key is null || key.GetValue(name) is null) return null;
        return key.GetValueKind(name) switch
        {
            RegistryValueKind.ExpandString => RegKind.ExpandString,
            RegistryValueKind.DWord => RegKind.DWord,
            RegistryValueKind.QWord => RegKind.QWord,
            RegistryValueKind.Binary => RegKind.Binary,
            RegistryValueKind.MultiString => RegKind.MultiString,
            _ => RegKind.String,
        };
    }

    public void SetValue(RegHive hive, string path, string name, object value, RegKind kind)
    {
        using var root = Base(hive);
        using var key = root.CreateSubKey(path, writable: true)
                        ?? throw new InvalidOperationException($"Cannot open {hive}\\{path}");
        key.SetValue(name, value, kind switch
        {
            RegKind.ExpandString => RegistryValueKind.ExpandString,
            RegKind.DWord => RegistryValueKind.DWord,
            RegKind.QWord => RegistryValueKind.QWord,
            RegKind.Binary => RegistryValueKind.Binary,
            RegKind.MultiString => RegistryValueKind.MultiString,
            _ => RegistryValueKind.String,
        });
    }

    public void DeleteValue(RegHive hive, string path, string name)
    {
        using var root = Base(hive);
        using var key = root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKey(RegHive hive, string path)
    {
        using var root = Base(hive);
        root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }

    public bool KeyExists(RegHive hive, string path)
    {
        using var root = Base(hive);
        using var key = root.OpenSubKey(path, writable: false);
        return key is not null;
    }

    public IReadOnlyList<string> GetValueNames(RegHive hive, string path)
    {
        using var root = Base(hive);
        using var key = root.OpenSubKey(path, writable: false);
        return key?.GetValueNames() ?? [];
    }
}

/// <summary>In-memory registry used by unit tests (and handy for dry runs).</summary>
public sealed class InMemoryRegistry : IRegistryAccess
{
    private readonly Dictionary<string, Dictionary<string, (object Value, RegKind Kind)>> _keys =
        new(StringComparer.OrdinalIgnoreCase);

    private static string K(RegHive hive, string path) => hive + "\\" + path.Trim('\\');

    public object? GetValue(RegHive hive, string path, string name) =>
        _keys.TryGetValue(K(hive, path), out var values) && values.TryGetValue(name, out var v) ? v.Value : null;

    public RegKind? GetKind(RegHive hive, string path, string name) =>
        _keys.TryGetValue(K(hive, path), out var values) && values.TryGetValue(name, out var v) ? v.Kind : null;

    public void SetValue(RegHive hive, string path, string name, object value, RegKind kind)
    {
        if (!_keys.TryGetValue(K(hive, path), out var values))
            _keys[K(hive, path)] = values = new Dictionary<string, (object, RegKind)>(StringComparer.OrdinalIgnoreCase);
        values[name] = (value, kind);
    }

    public void DeleteValue(RegHive hive, string path, string name)
    {
        if (_keys.TryGetValue(K(hive, path), out var values)) values.Remove(name);
    }

    public void DeleteKey(RegHive hive, string path)
    {
        var prefix = K(hive, path);
        foreach (var key in _keys.Keys.Where(k => k.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                     || k.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
            _keys.Remove(key);
    }

    public bool KeyExists(RegHive hive, string path) => _keys.ContainsKey(K(hive, path));

    public IReadOnlyList<string> GetValueNames(RegHive hive, string path) =>
        _keys.TryGetValue(K(hive, path), out var values) ? values.Keys.ToList() : [];
}
