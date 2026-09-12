using PenDynamicsLab.Curves;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenDynamicsLab.Persistence;

public sealed record UserPreset(string Name, PressureCurveParams Params);

/// <summary>
/// What <see cref="PresetStore.Rename"/> did, and when it did nothing, why.
/// </summary>
/// <remarks>
/// A bool cannot carry this. The caller has to tell the three refusals apart to say anything
/// useful about them, and it has to tell <see cref="Unchanged"/> — a confirmed rename to the
/// name the preset already has — apart from a refusal, because that one is not a mistake and
/// the caller should close its name box rather than report an error.
/// </remarks>
public enum RenameOutcome
{
    /// <summary>The preset now carries the new name, and the file has been written.</summary>
    Renamed,

    /// <summary>The new name equals the old one. Nothing to do, and nothing wrong.</summary>
    Unchanged,

    /// <summary>The new name is empty once trimmed.</summary>
    NameBlank,

    /// <summary>Another preset already has the new name.</summary>
    NameTaken,

    /// <summary>No preset carries the old name.</summary>
    NoSuchPreset,
}

/// <summary>
/// Persists user-saved curve presets as JSON in
/// <c>%LOCALAPPDATA%\PenDynamicsLab\presets.json</c>. Mirrors WebPressureExplorer's
/// localStorage-based preset store.
/// </summary>
public sealed class PresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // The params converter reads presets saved before curve 2 existed, mapping their
        // flat curve fields onto Curve1. See PressureCurveParamsConverter.
        Converters = { new JsonStringEnumConverter(), new PressureCurveParamsConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string FilePath { get; }

    private List<UserPreset> _presets = new();

    public PresetStore() : this(DefaultPath()) { }

    public PresetStore(string filePath)
    {
        FilePath = filePath;
        Load();
    }

    private static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PenDynamicsLab");
        return Path.Combine(dir, "presets.json");
    }

    public IReadOnlyList<UserPreset> All => _presets;

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { _presets = new(); return; }
            var json = File.ReadAllText(FilePath);
            _presets = JsonSerializer.Deserialize<List<UserPreset>>(json, JsonOptions) ?? new();
        }
        catch
        {
            // A corrupt or unreadable file shouldn't crash the app — start empty and let the user
            // re-save. We deliberately swallow here because the alternative (popping a dialog from
            // the constructor) is worse.
            _presets = new();
        }
    }

    public void Save(string name, PressureCurveParams parameters)
    {
        name = name.Trim();
        if (name.Length == 0) return;

        var entry = new UserPreset(name, parameters);
        int existing = _presets.FindIndex(p => p.Name == name);
        if (existing >= 0) _presets[existing] = entry;
        else _presets.Add(entry);
        Persist();
    }

    public void Delete(string name)
    {
        _presets.RemoveAll(p => p.Name == name);
        Persist();
    }

    /// <summary>
    /// Rename a preset in place, keeping its position in the list. Returns what happened, so a
    /// caller can report a refusal rather than present one as a rename that took.
    /// </summary>
    /// <remarks>
    /// Names are the identity here — <see cref="Save"/> and <see cref="Get"/> both match on
    /// them — so two presets may not share one.
    /// </remarks>
    public RenameOutcome Rename(string oldName, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0) return RenameOutcome.NameBlank;

        int index = _presets.FindIndex(p => p.Name == oldName);
        if (index < 0) return RenameOutcome.NoSuchPreset;

        // Checked after the lookup so that confirming the existing name reads as Unchanged
        // rather than NameTaken. The preset it collides with is itself.
        if (newName == oldName) return RenameOutcome.Unchanged;
        if (_presets.Any(p => p.Name == newName)) return RenameOutcome.NameTaken;

        _presets[index] = _presets[index] with { Name = newName };
        Persist();
        return RenameOutcome.Renamed;
    }

    /// <summary>
    /// An unused name of the form "Preset 1", "Preset 2", ... so saving can be one click
    /// rather than a naming prompt. Renaming afterwards is the deliberate step.
    /// </summary>
    public string NextAvailableName(string prefix = "Preset")
    {
        for (int i = 1; ; i++)
        {
            var candidate = $"{prefix} {i}";
            if (!_presets.Any(p => p.Name == candidate)) return candidate;
        }
    }

    public UserPreset? Get(string name) => _presets.FirstOrDefault(p => p.Name == name);

    private void Persist()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_presets, JsonOptions));
    }
}
