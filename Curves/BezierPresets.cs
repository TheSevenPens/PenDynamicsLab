using System.Collections.Immutable;

namespace PenDynamicsLab.Curves;

public sealed record BezierPreset(string Name, ImmutableArray<BezierPoint> Points);

public static class BezierPresets
{
    public static readonly ImmutableArray<BezierPreset> All =
    [
        new BezierPreset("Linear",
        [
            new BezierPoint(0, 0, 0,    0,    0.33, 0.33, HandleMode.Broken),
            new BezierPoint(1, 1, 0.67, 0.67, 1,    1,    HandleMode.Broken),
        ]),
        new BezierPreset("Soft",
        [
            new BezierPoint(0, 0, 0,   0, 0.1, 0.5, HandleMode.Broken),
            new BezierPoint(1, 1, 0.5, 1, 1,   1,   HandleMode.Broken),
        ]),
        new BezierPreset("Firm",
        [
            new BezierPoint(0, 0, 0,   0,   0.5, 0,   HandleMode.Broken),
            new BezierPoint(1, 1, 0.9, 0.5, 1,   1,   HandleMode.Broken),
        ]),
        new BezierPreset("S-Curve",
        [
            new BezierPoint(0, 0, 0,    0, 0.45, 0, HandleMode.Broken),
            new BezierPoint(1, 1, 0.55, 1, 1,    1, HandleMode.Broken),
        ]),
        new BezierPreset("Light Touch",
        [
            new BezierPoint(0, 0, 0,   0, 0.05, 0.7, HandleMode.Broken),
            new BezierPoint(1, 1, 0.3, 1, 1,    1,   HandleMode.Broken),
        ]),
        new BezierPreset("Heavy",
        [
            new BezierPoint(0, 0, 0,    0,   0.7, 0,   HandleMode.Broken),
            new BezierPoint(1, 1, 0.95, 0.3, 1,   1,   HandleMode.Broken),
        ]),
        new BezierPreset("Step",
        [
            new BezierPoint(0,   0,    0,    0,    0.15, 0,    HandleMode.Broken),
            new BezierPoint(0.4, 0.05, 0.3,  0.05, 0.45, 0.05, HandleMode.Broken),
            new BezierPoint(0.6, 0.95, 0.55, 0.95, 0.7,  0.95, HandleMode.Broken),
            new BezierPoint(1,   1,    0.85, 1,    1,    1,    HandleMode.Broken),
        ]),
    ];
}
