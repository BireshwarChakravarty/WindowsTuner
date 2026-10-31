using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsTuner.Core.Infrastructure;

namespace WindowsTuner.Core.Tweaks;

public enum TweakRisk { Safe, Moderate, Advanced }

public enum RestartNeed { None, Explorer, SignOut, Reboot }

public enum TweakState { Off, On, Partial, NotApplicable }

/// <summary>One registry value a tweak sets. <c>off: null</c> (or omitted) means "delete the value" to restore the default.</summary>
public sealed class RegistryChange
{
    public string Hive { get; set; } = "HKCU";
    public string Path { get; set; } = "";
    /// <summary>Value name; empty string means the key's (Default) value.</summary>
    public string Name { get; set; } = "";
    public RegKind Kind { get; set; } = RegKind.DWord;
    public JsonElement? On { get; set; }
    public JsonElement? Off { get; set; }
    /// <summary>When turning off, delete the whole key (used e.g. for the classic context menu CLSID).</summary>
    public bool DeleteKeyWhenOff { get; set; }
    /// <summary>Only used to detect state; the change itself is made by the tweak's commands.</summary>
    public bool DetectOnly { get; set; }

    [JsonIgnore] public RegHive ParsedHive => RegHiveParser.Parse(Hive);
}

public sealed class TweakDefinition
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "Other";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public TweakRisk Risk { get; set; } = TweakRisk.Safe;
    public RestartNeed Restart { get; set; } = RestartNeed.None;
    /// <summary>Recommended in the "speed up my PC" preset.</summary>
    public bool Recommended { get; set; }
    public int? MinBuild { get; set; }
    public int? MaxBuild { get; set; }
    public List<RegistryChange> Registry { get; set; } = [];
    public List<string> OnCommands { get; set; } = [];
    public List<string> OffCommands { get; set; } = [];
    /// <summary>True for tweaks loaded from the user's custom-tweaks.json.</summary>
    [JsonIgnore] public bool IsCustom { get; set; }

    public bool IsApplicable(int osBuild) =>
        (MinBuild is null || osBuild >= MinBuild) && (MaxBuild is null || osBuild <= MaxBuild);
}

public sealed class TweakCatalogFile
{
    public int Version { get; set; } = 1;
    public List<TweakDefinition> Tweaks { get; set; } = [];
}

/// <summary>UI wrapper around a tweak with its live state.</summary>
public sealed class TweakItem(TweakDefinition definition) : ObservableObject
{
    private TweakState _state;
    private bool _isBusy;

    public TweakDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Category => Definition.Category;
    public string Description => Definition.Description;
    public TweakRisk Risk => Definition.Risk;
    public RestartNeed Restart => Definition.Restart;
    public bool IsCustom => Definition.IsCustom;

    public string RestartText => Restart switch
    {
        RestartNeed.Explorer => "Restarts Explorer",
        RestartNeed.SignOut => "Sign out to apply",
        RestartNeed.Reboot => "Restart PC to apply",
        _ => "",
    };

    public TweakState State
    {
        get => _state;
        set
        {
            if (!Set(ref _state, value)) return;
            OnPropertyChanged(nameof(IsOn));
            OnPropertyChanged(nameof(SwitchState));
            OnPropertyChanged(nameof(IsApplicable));
            OnPropertyChanged(nameof(StateText));
        }
    }

    public bool IsApplicable => State != TweakState.NotApplicable;
    public bool IsOn => State == TweakState.On;
    /// <summary>For a three-state switch: partly applied tweaks show as indeterminate.</summary>
    public bool? SwitchState => State switch { TweakState.On => true, TweakState.Partial => null, _ => false };
    public bool IsRecommended => Definition.Recommended;
    public string StateText => State switch
    {
        TweakState.On => "On",
        TweakState.Partial => "Partly on",
        TweakState.NotApplicable => "Not for this Windows version",
        _ => "Off",
    };

    public bool IsBusy { get => _isBusy; set => Set(ref _isBusy, value); }
}

public sealed class TweakProfile
{
    public string Name { get; set; } = "My profile";
    public DateTime Created { get; set; } = DateTime.Now;
    public string? Computer { get; set; }
    public Dictionary<string, bool> Tweaks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
