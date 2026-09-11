namespace PenDynamicsLab.Drawing;

/// <summary>How each new stroke picks its colour.</summary>
/// <remarks>
/// This and <see cref="PressureControl"/> used to live in <c>Curves/Enums.cs</c>, next to
/// curve types they have nothing to do with. They are brush concepts: what a mark looks like,
/// not how pressure is shaped on the way to it.
/// </remarks>
public enum ColorMode
{
    Black,
    Red,

    /// <summary>A colour chosen per stroke. The chosen colour is not recoverable from settings.</summary>
    Random,
}

/// <summary>Which property of the mark the pressure signal drives.</summary>
public enum PressureControl
{
    /// <summary>Pressure sets stroke width; opacity stays at 1.</summary>
    Size,

    /// <summary>Pressure sets opacity; stroke width stays at the brush size.</summary>
    Opacity,
}
