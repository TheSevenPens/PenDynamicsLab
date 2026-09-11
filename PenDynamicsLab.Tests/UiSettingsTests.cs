using PenDynamicsLab.Curves;
using PenDynamicsLab.Persistence;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// The theme preference has to survive a restart, and a settings file that predates it
/// has to keep working. Both are round-trip properties of <see cref="UiSettings"/>.
/// </summary>
public class UiSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pdl-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Theme_DefaultsToSystem_OnAFreshInstall()
        => Assert.Equal(AppTheme.System, new UiSettings(_path).Theme);

    [Theory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.System)]
    public void Theme_SurvivesAReload(AppTheme theme)
    {
        // Set it to something else first, so writing System is a real change and not a
        // no-op that would pass whether or not the value was persisted.
        var first = new UiSettings(_path) { Theme = AppTheme.Dark };
        first.Theme = theme;

        Assert.Equal(theme, new UiSettings(_path).Theme);
    }

    [Fact]
    public void Theme_IsStoredByName_SoAddingOneCannotRepointSavedPreferences()
    {
        new UiSettings(_path).Theme = AppTheme.Dark;
        Assert.Contains("\"Dark\"", File.ReadAllText(_path));
    }

    [Fact]
    public void SettingsFileWithoutATheme_LoadsAsSystem()
    {
        // What an install from before this feature has on disk.
        File.WriteAllText(_path, "{ \"DriverTipDismissed\": true }");

        var settings = new UiSettings(_path);
        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.True(settings.DriverTipDismissed);
    }

    [Fact]
    public void TheTwoPreferencesDoNotOverwriteEachOther()
    {
        var settings = new UiSettings(_path) { Theme = AppTheme.Light };
        settings.DriverTipDismissed = true;

        var reloaded = new UiSettings(_path);
        Assert.Equal(AppTheme.Light, reloaded.Theme);
        Assert.True(reloaded.DriverTipDismissed);
    }

    // ── Curve count ──────────────────────────────────────────────

    [Fact]
    public void UseTwoCurves_DefaultsToOff()
        // One curve is what most sessions want; the second exists for specific comparisons.
        => Assert.False(new UiSettings(_path).UseTwoCurves);

    [Fact]
    public void UseTwoCurves_SurvivesAReload()
    {
        new UiSettings(_path).UseTwoCurves = true;
        Assert.True(new UiSettings(_path).UseTwoCurves);
    }

    [Fact]
    public void SettingsFileWithoutACurveCount_LoadsAsOne()
    {
        // What an install from before this setting has on disk.
        File.WriteAllText(_path, "{ \"Theme\": \"Dark\" }");

        var settings = new UiSettings(_path);
        Assert.False(settings.UseTwoCurves);
        Assert.Equal(AppTheme.Dark, settings.Theme);
    }

    [Fact]
    public void TheThreePreferencesDoNotOverwriteEachOther()
    {
        var settings = new UiSettings(_path) { Theme = AppTheme.Light };
        settings.DriverTipDismissed = true;
        settings.UseTwoCurves = true;

        var reloaded = new UiSettings(_path);
        Assert.Equal(AppTheme.Light, reloaded.Theme);
        Assert.True(reloaded.DriverTipDismissed);
        Assert.True(reloaded.UseTwoCurves);
    }

    // ── Processing order ─────────────────────────────────────────

    [Fact]
    public void SmoothingOrder_DefaultsToSmoothFirst()
        => Assert.Equal(SmoothingOrder.SmoothThenCurve, new UiSettings(_path).SmoothingOrder);

    [Fact]
    public void SmoothingOrder_SurvivesAReload()
    {
        new UiSettings(_path).SmoothingOrder = SmoothingOrder.CurveThenSmooth;
        Assert.Equal(SmoothingOrder.CurveThenSmooth, new UiSettings(_path).SmoothingOrder);
    }

    [Fact]
    public void SmoothingOrder_IsStoredByName()
    {
        new UiSettings(_path).SmoothingOrder = SmoothingOrder.CurveThenSmooth;
        Assert.Contains("\"CurveThenSmooth\"", File.ReadAllText(_path));
    }

    [Fact]
    public void AnUnreadableFile_FallsBackToDefaults_RatherThanThrowing()
    {
        File.WriteAllText(_path, "not json at all");
        Assert.Equal(AppTheme.System, new UiSettings(_path).Theme);
    }
}
