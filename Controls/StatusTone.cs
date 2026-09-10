namespace PenDynamicsLab.Controls;

/// <summary>How a <see cref="SectionCard"/> status pill is toned.</summary>
public enum StatusTone
{
    /// <summary>Grey — the stage is contributing nothing right now.</summary>
    Neutral,

    /// <summary>Accent — the stage is doing work.</summary>
    Active,

    /// <summary>Amber — running, but configured so that nothing changes.</summary>
    Advisory,
}
