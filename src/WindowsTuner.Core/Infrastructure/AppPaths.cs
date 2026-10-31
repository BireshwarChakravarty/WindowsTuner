namespace WindowsTuner.Core.Infrastructure;

public static class AppPaths
{
    private static string? _root;

    /// <summary>%LOCALAPPDATA%\WindowsTuner (overridable for tests).</summary>
    public static string Root
    {
        get
        {
            if (_root is null)
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(local)) local = Path.GetTempPath();
                _root = Path.Combine(local, "WindowsTuner");
            }
            Directory.CreateDirectory(_root);
            return _root;
        }
        set => _root = value;
    }

    public static string Logs => Ensure(Path.Combine(Root, "Logs"));
    public static string Backups => Ensure(Path.Combine(Root, "Backups"));
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string TweakJournalFile => Path.Combine(Root, "tweak-journal.json");
    public static string CustomTweaksFile => Path.Combine(Root, "custom-tweaks.json");
    public static string ProfilesDir => Ensure(Path.Combine(Root, "Profiles"));

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
