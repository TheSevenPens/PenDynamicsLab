namespace PenDynamicsLab.Curves;

public sealed record BezierPoint(
    double X,
    double Y,
    double InX,
    double InY,
    double OutX,
    double OutY,
    HandleMode HandleMode);
