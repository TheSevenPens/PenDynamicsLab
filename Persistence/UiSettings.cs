using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenDynamicsLab.Persistence;

/// <summary>Which theme the user asked for. <see cref="System"/> follows the OS setting.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

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

        /// <summary>
        /// Defaults to <see cref="AppTheme.System"/>, so a first run looks like the rest
        /// of the desktop rather than announcing an opinion the user never expressed.
        /// </summary>
        public AppTheme Theme { get; init; } = AppTheme.System;

        /// <summary>
        /// Whether the pipeline shows a second curve. Off by default: one curve is what
        /// most sessions want, and the second one exists for specific comparisons.
        /// </summary>
        public bool UseTwoCurves { get; init; }
    }

    // Enums as names, not ordinals: the file is meant to be readable, and adding a theme
    // in the middle of the enum must not silently repoint everyone's saved preference.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

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

    public AppTheme Theme
    {
        get => _model.Theme;
        set
        {
            if (_model.Theme == value) return;
            _model = _model with { Theme = value };
            Save();
        }
    }

    public bool UseTwoCurves
    {
        get => _model.UseTwoCurves;
        set
        {
            if (_model.UseTwoCurves == value) return;
            _model = _model with { UseTwoCurves = value };
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
