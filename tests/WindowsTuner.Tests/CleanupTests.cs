using WindowsTuner.Core.Cleanup;

namespace WindowsTuner.Tests;

public class CleanupTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wt-clean").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Make(string relative, int size, TimeSpan? age = null)
    {
        var path = Path.Combine(_root, relative.Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        if (age is { } a) File.SetLastWriteTimeUtc(path, DateTime.UtcNow - a);
        return path;
    }

    [Fact]
    public void ExpandsWildcardFolders()
    {
        Make(@"User Data\Default\Cache\a.bin", 10);
        Make(@"User Data\Profile 1\Cache\b.bin", 10);
        Make(@"User Data\Profile 2\NoCache\c.bin", 10);
        var dirs = FileScanner.ExpandPattern(Path.Combine(_root, "User Data", "*", "Cache"));
        Assert.Equal(2, dirs.Count);
    }

    [Fact]
    public void ExpandsEnvironmentVariables()
    {
        Environment.SetEnvironmentVariable("WT_TEST_ROOT", _root);
        Make(@"Temp\x.tmp", 1);
        Assert.Single(FileScanner.ExpandPattern("%WT_TEST_ROOT%/Temp"));
        Assert.Empty(FileScanner.ExpandPattern("%WT_UNDEFINED_VAR_XYZ%/Temp"));
    }

    [Fact]
    public void MeasuresAndDeletesRespectingAge()
    {
        Make(@"Temp\old1.tmp", 100, TimeSpan.FromDays(3));
        Make(@"Temp\sub\old2.tmp", 50, TimeSpan.FromDays(3));
        var fresh = Make(@"Temp\fresh.tmp", 1000);
        var dirs = new[] { Path.Combine(_root, "Temp") };

        var (bytes, files) = FileScanner.Measure(dirs, "*", TimeSpan.FromHours(24));
        Assert.Equal(150, bytes);
        Assert.Equal(2, files);

        var result = FileScanner.Delete(dirs, "*", TimeSpan.FromHours(24));
        Assert.Equal(150, result.BytesFreed);
        Assert.Equal(2, result.FilesDeleted);
        Assert.True(File.Exists(fresh));
        Assert.False(Directory.Exists(Path.Combine(_root, "Temp", "sub"))); // empty sub-folder removed
        Assert.True(Directory.Exists(Path.Combine(_root, "Temp")));       // root kept
    }

    [Fact]
    public void FilePatternIsHonoured()
    {
        Make(@"Explorer\thumbcache_256.db", 10);
        Make(@"Explorer\iconcache_256.db", 10);
        var (_, files) = FileScanner.Measure([Path.Combine(_root, "Explorer")], "thumbcache_*.db", TimeSpan.Zero);
        Assert.Equal(1, files);
    }

    [Fact]
    public void DefaultTargetsAreWellFormed()
    {
        var targets = CleanupService.CreateDefaultTargets();
        Assert.Equal(targets.Count, targets.Select(t => t.Id).Distinct().Count());
        Assert.All(targets.Where(t => t.Kind == CleanupKind.Files), t => Assert.NotEmpty(t.Paths));
        Assert.DoesNotContain(targets, t => t.Kind == CleanupKind.RecycleBin && t.DefaultSelected);
    }
}
