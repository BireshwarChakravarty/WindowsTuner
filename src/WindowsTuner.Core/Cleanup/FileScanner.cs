namespace WindowsTuner.Core.Cleanup;

/// <summary>Cross-platform file enumeration/deletion used by the cleaner (unit-tested on any OS).</summary>
public static class FileScanner
{
    private static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // Never follow junctions/symlinks out of the target folder.
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    /// <summary>Expands %ENV% variables and '*'/'?' wildcards inside folder segments.</summary>
    public static IReadOnlyList<string> ExpandPattern(string pattern)
    {
        var expanded = Environment.ExpandEnvironmentVariables(pattern);
        if (expanded.Contains('%')) return []; // variable not defined on this machine
        expanded = expanded.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        var root = Path.GetPathRoot(expanded) ?? "";
        var rest = expanded[root.Length..];
        var segments = rest.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var current = new List<string> { root.Length == 0 ? Directory.GetCurrentDirectory() : root };
        foreach (var segment in segments)
        {
            var next = new List<string>();
            foreach (var dir in current)
            {
                if (segment.Contains('*') || segment.Contains('?'))
                {
                    try
                    {
                        if (Directory.Exists(dir))
                            next.AddRange(Directory.EnumerateDirectories(dir, segment, new EnumerationOptions
                            {
                                IgnoreInaccessible = true,
                                AttributesToSkip = FileAttributes.ReparsePoint,
                            }));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                else
                {
                    next.Add(Path.Combine(dir, segment));
                }
            }
            current = next;
            if (current.Count == 0) break;
        }
        return current.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static IEnumerable<FileInfo> EnumerateFiles(string directory, string filePattern, TimeSpan minAge)
    {
        if (!Directory.Exists(directory)) yield break;
        var cutoff = DateTime.UtcNow - minAge;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, filePattern, Recursive);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var path in files)
        {
            FileInfo? info = null;
            try
            {
                info = new FileInfo(path);
                if (minAge > TimeSpan.Zero && info.LastWriteTimeUtc > cutoff) info = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            if (info is not null) yield return info;
        }
    }

    public static (long Bytes, int Files) Measure(IEnumerable<string> directories, string filePattern, TimeSpan minAge, CancellationToken ct = default)
    {
        long bytes = 0;
        var count = 0;
        foreach (var dir in directories)
        {
            foreach (var f in EnumerateFiles(dir, filePattern, minAge))
            {
                ct.ThrowIfCancellationRequested();
                try { bytes += f.Length; count++; } catch (IOException) { }
            }
        }
        return (bytes, count);
    }

    /// <summary>Deletes matching files (skipping locked ones), then removes empty sub-folders. The root folders are kept.</summary>
    public static CleanResult Delete(IEnumerable<string> directories, string filePattern, TimeSpan minAge, CancellationToken ct = default)
    {
        long freed = 0;
        int deleted = 0, skipped = 0;
        foreach (var dir in directories)
        {
            foreach (var f in EnumerateFiles(dir, filePattern, minAge).ToList())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var len = f.Length;
                    if (f.IsReadOnly) f.IsReadOnly = false;
                    f.Delete();
                    freed += len;
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++; // in use — that's expected for temp files
                }
            }
            RemoveEmptyDirectories(dir, isRoot: true);
        }
        return new CleanResult(freed, deleted, skipped);
    }

    private static void RemoveEmptyDirectories(string dir, bool isRoot)
    {
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir, "*", new EnumerationOptions
                     { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList())
                RemoveEmptyDirectories(sub, isRoot: false);
            if (!isRoot && !Directory.EnumerateFileSystemEntries(dir).Any())
                Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
