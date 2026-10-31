using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Updates;

public enum UpdateStatus { Available, Updating, Updated, Failed, Skipped }

/// <summary>An application that winget reports as having a newer version.</summary>
public sealed class AppUpdate : ObservableObject
{
    private bool _isSelected = true;
    private UpdateStatus _status;
    private string _message = "";

    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string InstalledVersion { get; init; }
    public required string AvailableVersion { get; init; }
    public string Source { get; init; } = "";

    /// <summary>winget lists these separately: pinned or "requires explicit targeting".</summary>
    public bool RequiresExplicitTarget { get; init; }

    public bool IsVersionUnknown =>
        InstalledVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || InstalledVersion.StartsWith('<');

    /// <summary>winget truncates long columns with an ellipsis; such IDs cannot be targeted exactly.</summary>
    public bool IsIdTruncated => Id.EndsWith('…');

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public UpdateStatus Status { get => _status; set => Set(ref _status, value); }
    public string Message { get => _message; set => Set(ref _message, value); }
}
