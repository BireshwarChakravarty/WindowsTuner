using System.Runtime.InteropServices;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Storage;

public sealed class FoundFile : ObservableObject
{
    private bool _isSelected;
    public required string Path { get; init; }
    public long SizeBytes { get; init; }
    public DateTime LastWrite { get; init; }
    public string Reason { get; init; } = "";
    public string Name => System.IO.Path.GetFileName(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string Size => Format.Bytes(SizeBytes);
    public string Modified => LastWrite.ToString("yyyy-MM-dd");
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>Finds what's eating your personal disk space: huge files and forgotten installers in Downloads.</summary>
public static class LargeFileFinder
{
    private static readonly string[] InstallerExtensions = [".exe", ".msi", ".msix", ".msixbundle", ".appx", ".iso", ".zip", ".7z", ".rar"];

    // Cloud placeholders (OneDrive "online-only") would be downloaded if we touched them.
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;

    public static IReadOnlyList<FoundFile> FindLargest(string root, long minBytes, int max, CancellationToken ct = default)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Offline | RecallOnDataAccess | RecallOnOpen | FileAttributes.System,
        };
        var top = new PriorityQueue<FoundFile, long>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                FileInfo fi;
                try { fi = new FileInfo(path); if (fi.Length < minBytes) continue; }
                catch { continue; }
                top.Enqueue(new FoundFile { Path = path, SizeBytes = fi.Length, LastWrite = fi.LastWriteTime, Reason = "Large file" }, fi.Length);
                if (top.Count > max) top.Dequeue();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        var result = new List<FoundFile>();
        while (top.Count > 0) result.Add(top.Dequeue());
        result.Reverse();
        return result;
    }

    public static IReadOnlyList<FoundFile> FindOldInstallers(string downloads, TimeSpan olderThan)
    {
        if (!Directory.Exists(downloads)) return [];
        var cutoff = DateTime.Now - olderThan;
        return Directory.EnumerateFiles(downloads, "*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false })
            .Select(p => new FileInfo(p))
            .Where(f => InstallerExtensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase) && f.LastWriteTime < cutoff)
            .OrderByDescending(f => f.Length)
            .Select(f => new FoundFile
            {
                Path = f.FullName, SizeBytes = f.Length, LastWrite = f.LastWriteTime,
                Reason = $"Old {f.Extension.TrimStart('.').ToUpperInvariant()} in Downloads",
            })
            .ToList();
    }

    public static string DownloadsFolder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    #region Recycle Bin delete (shell32)

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    private const uint FoDelete = 0x0003;
    private const ushort FofAllowUndo = 0x0040, FofNoConfirmation = 0x0010, FofSilent = 0x0004, FofNoErrorUi = 0x0400;

    /// <summary>Moves files to the Recycle Bin (recoverable).</summary>
    public static bool RecycleFiles(IEnumerable<string> paths)
    {
        var list = paths.Where(File.Exists).ToList();
        if (list.Count == 0) return true;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FoDelete,
            pFrom = string.Join('\0', list) + "\0\0",
            fFlags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
        };
        return SHFileOperation(ref op) == 0;
    }

    #endregion
}
