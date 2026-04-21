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
