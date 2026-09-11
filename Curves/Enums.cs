namespace PenDynamicsLab.Curves;

public enum CurveType
{
    Passthrough,
    Flat,
    Basic,
    Extended,
    Inverted,
    Sigmoid,
    Bezier,
}

public enum MinApproach
{
    Clamp,
    Cut,
}

public enum HandleMode
{
    Mirrored,
    Broken,
}

/// <summary>
/// Smoothing algorithm. <see cref="Passthrough"/> is the counterpart of
/// <see cref="CurveType.Passthrough"/>: input passes through untouched, whatever the
/// smoothing amount says.
/// </summary>
public enum SmoothingType
{
    Passthrough,
    Ema,
}

public enum SmoothingOrder
{
    SmoothThenCurve,
    CurveThenSmooth,
}

// ColorMode and PressureControl moved to Drawing/Enums.cs - they are brush concepts,
// not curve ones, and drawing code should not have to reach into Curves/ for them.
