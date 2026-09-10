namespace PenDynamicsLab.Curves;

public enum CurveType
{
    Passthrough,
    Flat,
    Basic,
    Extended,
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

public enum ColorMode
{
    Black,
    Random,
}

public enum PressureControl
{
    Size,
    Opacity,
}
