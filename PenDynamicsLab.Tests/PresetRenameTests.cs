using PenDynamicsLab.Curves;
using PenDynamicsLab.Persistence;
using Xunit;

namespace PenDynamicsLab.Tests;

/// <summary>
/// Pins what <see cref="PresetStore.Rename"/> reports, because the caller acts on it.
/// </summary>
/// <remarks>
/// The rename handler used to discard the return value and close its name box either way, so a
/// collision looked exactly like a rename that took: the box shut, the list rebuilt, and the
/// preset still carried its old name. A bool could not have told the caller enough to say why.
/// </remarks>
public class PresetRenameTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"pdl-rename-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private PresetStore StoreWith(params string[] names)
    {
        var store = new PresetStore(_path);
        foreach (var n in names) store.Save(n, new PressureCurveParams());
        return store;
    }

    [Fact]
    public void Renaming_to_a_free_name_renames_and_keeps_the_position()
    {
        var store = StoreWith("Preset 1", "Preset 2", "Preset 3");

        Assert.Equal(RenameOutcome.Renamed, store.Rename("Preset 2", "Soft touch"));
        Assert.Equal(["Preset 1", "Soft touch", "Preset 3"], store.All.Select(p => p.Name));
    }

    [Fact]
    public void A_rename_survives_a_reload()
    {
        StoreWith("Preset 1").Rename("Preset 1", "Soft touch");

        Assert.Equal("Soft touch", new PresetStore(_path).All.Single().Name);
    }

    [Fact]
    public void Renaming_onto_another_preset_is_refused_and_says_so()
    {
        // The failing case from the report: the handler closed its box on this and the list
        // still showed both original names.
        var store = StoreWith("Preset 1", "Preset 2");

        Assert.Equal(RenameOutcome.NameTaken, store.Rename("Preset 2", "Preset 1"));
        Assert.Equal(["Preset 1", "Preset 2"], store.All.Select(p => p.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_is_refused(string name)
    {
        var store = StoreWith("Preset 1");

        Assert.Equal(RenameOutcome.NameBlank, store.Rename("Preset 1", name));
        Assert.Equal("Preset 1", store.All.Single().Name);
    }

    [Fact]
    public void Confirming_the_name_it_already_has_is_not_a_refusal()
    {
        // Separated from NameTaken so the caller can close its name box on this one. The
        // preset the new name collides with is the preset being renamed.
        var store = StoreWith("Preset 1");

        Assert.Equal(RenameOutcome.Unchanged, store.Rename("Preset 1", "Preset 1"));
        Assert.Equal("Preset 1", store.All.Single().Name);
    }

    [Fact]
    public void Renaming_a_preset_that_is_gone_reports_that_rather_than_a_collision()
    {
        var store = StoreWith("Preset 1");

        Assert.Equal(RenameOutcome.NoSuchPreset, store.Rename("Deleted", "Preset 1"));
    }

    [Fact]
    public void Surrounding_space_is_trimmed_from_the_new_name()
    {
        var store = StoreWith("Preset 1");

        Assert.Equal(RenameOutcome.Renamed, store.Rename("Preset 1", "  Soft touch  "));
        Assert.Equal("Soft touch", store.All.Single().Name);
    }
}
