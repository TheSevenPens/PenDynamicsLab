using System.IO;
using System.Text.Json;

namespace PenDynamicsLab.Persistence;

/// <summary>
/// Small persisted bag of UI preferences, kept beside the presets in
/// <c>%LOCALAPPDATA%\PenDynamicsLab\ui-settings.json</c>.
/// </summary>
/// <remarks>
/// Separate from <see cref="PresetStore"/> on purpose: presets are user content they
/// name and manage, these are preferences the app remembers on their behalf. Every read
/// and write is best-effort — a preference failing to persist must never stop the app.
/// </remarks>
public sealed class UiSettings
{
    private sealed record Model
    {
        /// <summary>True once the user has chosen "Don't show again" on the driver tip.</summary>
        public bool DriverTipDismissed { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private Model _model = new();

    public UiSettings() : this(DefaultPath()) { }

    public UiSettings(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public bool DriverTipDismissed
    {
        get => _model.DriverTipDismissed;
        set
        {
            if (_model.DriverTipDismissed == value) return;
            _model = _model with { DriverTipDismissed = value };
            Save();
        }
    }

    private static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PenDynamicsLab");
        return Path.Combine(dir, "ui-settings.json");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            _model = JsonSerializer.Deserialize<Model>(File.ReadAllText(_filePath), JsonOptions) ?? new Model();
        }
        catch
        {
            _model = new Model();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_model, JsonOptions));
        }
        catch
        {
            // Preferences are a convenience; losing one is not worth surfacing.
        }
    }
}
