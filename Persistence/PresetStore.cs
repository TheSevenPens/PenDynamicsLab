using PenDynamicsLab.Curves;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenDynamicsLab.Persistence;

public sealed record UserPreset(string Name, PressureCurveParams Params);

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
        Converters = { new JsonStringEnumConverter() },
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

    public UserPreset? Get(string name) => _presets.FirstOrDefault(p => p.Name == name);

    private void Persist()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_presets, JsonOptions));
    }
}
