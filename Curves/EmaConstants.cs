namespace PenDynamicsLab.Curves;

public static class EmaConstants
{
    public const double Max = 0.99;
    public const double MidTarget = 0.8;
    public static readonly double CurveExponent = Math.Log(MidTarget / Max) / Math.Log(0.5);
}
