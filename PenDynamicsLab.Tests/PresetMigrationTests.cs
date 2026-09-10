using PenDynamicsLab.Curves;
using PenDynamicsLab.Persistence;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Presets saved before curve 2 existed have to keep working, and keep meaning the same
/// thing. This is the one place a model change can quietly rewrite the user's own content.
/// </summary>
public class PresetMigrationTests : IDisposable
{
    private const double Eps = 1e-9;

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"pdl-presets-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    /// <summary>Exactly the shape the app wrote before this change: one curve, fields flat.</summary>
    private const string LegacyJson = """
    [
      {
        "Name": "Soft touch",
        "Params": {
          "SmoothingType": "Ema",
          "EmaSmoothing": 0.4,
          "SmoothingOrder": "SmoothThenCurve",
          "Softness": 0.6,
          "InputMinimum": 0.1,
          "InputMaximum": 0.9,
          "Minimum": 0.05,
          "Maximum": 0.95,
          "CurveType": "Extended",
          "MinApproach": "Cut",
          "FlatLevel": 0.5
        }
      }
    ]
    """;

    private PresetStore LoadLegacy()
    {
        File.WriteAllText(_path, LegacyJson);
        return new PresetStore(_path);
    }

    [Fact]
    public void ALegacyPreset_LoadsItsCurveIntoCurve1()
    {
        var p = LoadLegacy().Get("Soft touch")!.Params;

        Assert.Equal(CurveType.Extended, p.Curve1.CurveType);
        Assert.Equal(0.6, p.Curve1.Softness);
        Assert.Equal(0.1, p.Curve1.InputMinimum);
        Assert.Equal(0.9, p.Curve1.InputMaximum);
        Assert.Equal(0.05, p.Curve1.Minimum);
        Assert.Equal(0.95, p.Curve1.Maximum);
        Assert.Equal(MinApproach.Cut, p.Curve1.MinApproach);
    }

    [Fact]
    public void ALegacyPreset_GetsCurve2Bypassed()
        => Assert.Equal(CurveType.Passthrough, LoadLegacy().Get("Soft touch")!.Params.Curve2.CurveType);

    [Fact]
    public void ALegacyPreset_KeepsItsSmoothing()
    {
        var p = LoadLegacy().Get("Soft touch")!.Params;
        Assert.Equal(SmoothingType.Ema, p.SmoothingType);
        Assert.Equal(0.4, p.EmaSmoothing);
        Assert.Equal(SmoothingOrder.SmoothThenCurve, p.SmoothingOrder);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ALegacyPreset_MapsPressureExactlyAsItUsedTo(double x)
    {
        // The point of the whole migration: the loaded pair must be the same function the
        // single curve was. Curve 2 at Passthrough is what makes that true.
        var p = LoadLegacy().Get("Soft touch")!.Params;

        Assert.Equal(CurveMath.ApplyCurve(x, p.Curve1), CurveMath.ApplyPressureCurve(x, p), Eps);
    }

    // ── Round-tripping the current shape ─────────────────────────

    [Fact]
    public void BothCurvesSurviveASaveAndReload()
    {
        var saved = new PressureCurveParams
        {
            Curve1 = new CurveSettings { CurveType = CurveType.Basic, Softness = 0.35 },
            Curve2 = new CurveSettings { CurveType = CurveType.Sigmoid, Softness = 0.5 },
            SmoothingType = SmoothingType.Ema,
            EmaSmoothing = 0.25,
        };
        new PresetStore(_path).Save("Pair", saved);

        var loaded = new PresetStore(_path).Get("Pair")!.Params;

        // Field by field, not record equality: CurveSettings holds an ImmutableArray of
        // bezier points, and ImmutableArray equality is by reference, so two records with
        // identical contents compare unequal after a round trip. See CurveSettings.
        Assert.Equal(saved.Curve1.CurveType, loaded.Curve1.CurveType);
        Assert.Equal(saved.Curve1.Softness, loaded.Curve1.Softness);
        Assert.Equal(saved.Curve2.CurveType, loaded.Curve2.CurveType);
        Assert.Equal(saved.Curve2.Softness, loaded.Curve2.Softness);
        Assert.True(saved.Curve1.BezierPoints.SequenceEqual(loaded.Curve1.BezierPoints));
        Assert.Equal(saved.SmoothingType, loaded.SmoothingType);
        Assert.Equal(saved.EmaSmoothing, loaded.EmaSmoothing);
    }

    [Fact]
    public void ResavingALegacyPreset_RewritesItInTheCurrentShape()
    {
        var store = LoadLegacy();
        store.Save("Soft touch", store.Get("Soft touch")!.Params);

        var text = File.ReadAllText(_path);
        Assert.Contains("\"Curve1\"", text);
        Assert.Contains("\"Curve2\"", text);
    }

    [Fact]
    public void EnumsAreStoredByName_SoAddingACurveTypeCannotRepointSavedPresets()
    {
        new PresetStore(_path).Save("Named", new PressureCurveParams
        {
            Curve1 = new CurveSettings { CurveType = CurveType.Sigmoid },
        });

        Assert.Contains("\"Sigmoid\"", File.ReadAllText(_path));
    }
}
