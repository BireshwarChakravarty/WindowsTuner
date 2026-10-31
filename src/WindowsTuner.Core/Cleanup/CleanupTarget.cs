using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Cleanup;

public enum CleanupKind { Files, RecycleBin, WindowsUpdateCache, DeliveryOptimization }

/// <summary>A category of disposable files. Paths may use %ENV% variables and '*' wildcards in folder names.</summary>
public sealed class CleanupTarget : ObservableObject
{
    private bool _isSelected;
    private long _sizeBytes = -1;
    private int _fileCount;
    private string _status = "";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public string Group { get; init; } = "System";
    public CleanupKind Kind { get; init; } = CleanupKind.Files;
    public IReadOnlyList<string> Paths { get; init; } = [];
    public string FilePattern { get; init; } = "*";
    /// <summary>Only delete files older than this (protects files an installer or app is using right now).</summary>
    public TimeSpan MinAge { get; init; } = TimeSpan.Zero;
    /// <summary>Process names (without .exe) that must be closed for this target to be cleaned.</summary>
    public IReadOnlyList<string> RequiresClosed { get; init; } = [];
    public bool DefaultSelected { get; init; }
    public bool NeedsAdmin { get; init; }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public long SizeBytes
    {
        get => _sizeBytes;
        set { if (Set(ref _sizeBytes, value)) OnPropertyChanged(nameof(SizeText)); }
    }

    public int FileCount { get => _fileCount; set => Set(ref _fileCount, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string SizeText => SizeBytes < 0 ? "—" : Format.Bytes(SizeBytes);
}

public sealed record CleanResult(long BytesFreed, int FilesDeleted, int FilesSkipped);
