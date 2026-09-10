using System.Collections.Immutable;

namespace PenDynamicsLab.Curves;

public static class CurveMath
{
    /// <summary>Sigmoid steepness per unit of softness: <c>k = Softness * 14</c>.</summary>
    public const double SigmoidSteepness = 14;

    /// <summary>Below this |k| the sigmoid degenerates to a straight line.</summary>
    public const double SigmoidLinearThreshold = 0.01;

    /// <summary>
    /// Whether a curve type exposes the input/output range controls.
    /// </summary>
    /// <remarks>
    /// Basic deliberately does not: it is defined as the power curve across the full
    /// [0, 1] range. The range fields still exist on the record, though, and can hold
    /// values left behind by Extended/Sigmoid or carried in a saved preset — so the
    /// evaluator consults this rather than the fields, and Basic is exactly what it says.
    /// </remarks>
    public static bool UsesRangeControls(CurveType type)
        => type is CurveType.Extended or CurveType.Sigmoid;

    public static double RawCurveOutput(double xNorm, CurveSettings p)
    {
        double curved;

        if (p.CurveType == CurveType.Sigmoid)
        {
            double k = p.Softness * SigmoidSteepness;
            if (Math.Abs(k) < SigmoidLinearThreshold)
            {
                curved = xNorm;
            }
            else
            {
                double s0 = Sig(0, k);
                double s1 = Sig(1, k);
                double range = s1 - s0;
                curved = Math.Abs(range) < 1e-10 ? xNorm : (Sig(xNorm, k) - s0) / range;
            }
        }
        else
        {
            double exponent = p.Softness >= 0 ? 1 - p.Softness : 1 / (1 + p.Softness);
            curved = Math.Pow(Math.Max(0, xNorm), exponent);
        }

        bool usesRange = UsesRangeControls(p.CurveType);
        double outMin = usesRange ? p.Minimum : 0;
        double outMax = usesRange ? p.Maximum : 1;
        return outMin + curved * (outMax - outMin);

        static double Sig(double t, double k) => 1.0 / (1.0 + Math.Exp(-k * (t - 0.5)));
    }

    public static ImmutableArray<BezierPoint> NormalizeBezierPoints(IReadOnlyList<BezierPoint>? points)
    {
        var source = points is { Count: > 0 }
            ? points
            : new[]
            {
                new BezierPoint(0, 0, 0, 0, 0, 0, HandleMode.Broken),
                new BezierPoint(1, 1, 1, 1, 1, 1, HandleMode.Broken),
            };

        var list = source
            .Where(pt => double.IsFinite(pt.X) && double.IsFinite(pt.Y))
            .Select(pt => new BezierPoint(
                X: Clamp01(pt.X),
                Y: Clamp01(pt.Y),
                InX: double.IsFinite(pt.InX) ? Clamp01(pt.InX) : Clamp01(pt.X),
                InY: double.IsFinite(pt.InY) ? Clamp01(pt.InY) : Clamp01(pt.Y),
                OutX: double.IsFinite(pt.OutX) ? Clamp01(pt.OutX) : Clamp01(pt.X),
                OutY: double.IsFinite(pt.OutY) ? Clamp01(pt.OutY) : Clamp01(pt.Y),
                HandleMode: pt.HandleMode == HandleMode.Mirrored ? HandleMode.Mirrored : HandleMode.Broken))
            .OrderBy(pt => pt.X)
            .ToList();

        if (list.Count == 0)
        {
            return
            [
                new BezierPoint(0, 0, 0, 0, 0.33, 0, HandleMode.Broken),
                new BezierPoint(1, 1, 0.67, 1, 1, 1, HandleMode.Broken),
            ];
        }

        if (list[0].X > 0)
        {
            var first = list[0];
            list.Insert(0, new BezierPoint(
                X: 0,
                Y: first.Y,
                InX: 0,
                InY: first.Y,
                OutX: Math.Min(1, first.X / 2),
                OutY: first.Y,
                HandleMode: HandleMode.Broken));
        }
        else
        {
            list[0] = list[0] with { X = 0 };
        }

        int lastIndex = list.Count - 1;
        if (list[lastIndex].X < 1)
        {
            var last = list[lastIndex];
            list.Add(new BezierPoint(
                X: 1,
                Y: last.Y,
                InX: Math.Max(0, (1 + last.X) / 2),
                InY: last.Y,
                OutX: 1,
                OutY: last.Y,
                HandleMode: HandleMode.Broken));
            lastIndex = list.Count - 1;
        }
        else
        {
            list[lastIndex] = list[lastIndex] with { X = 1 };
        }

        for (int i = 0; i < list.Count; i++)
        {
            var pt = list[i];
            double prevX = i > 0 ? list[i - 1].X : pt.X;
            double nextX = i < list.Count - 1 ? list[i + 1].X : pt.X;
            list[i] = pt with
            {
                InX = Math.Max(prevX, Math.Min(pt.X, pt.InX)),
                OutX = Math.Max(pt.X, Math.Min(nextX, pt.OutX)),
                InY = Clamp01(pt.InY),
                OutY = Clamp01(pt.OutY),
                HandleMode = pt.HandleMode == HandleMode.Mirrored ? HandleMode.Mirrored : HandleMode.Broken,
            };
        }

        list[0] = list[0] with
        {
            InX = list[0].X,
            InY = list[0].Y,
            HandleMode = HandleMode.Broken,
        };
        list[lastIndex] = list[lastIndex] with
        {
            OutX = list[lastIndex].X,
            OutY = list[lastIndex].Y,
            HandleMode = HandleMode.Broken,
        };

        return [..list];
    }

    private readonly record struct CubicSegment(BezierPoint P0, (double X, double Y) C0, (double X, double Y) C1, BezierPoint P1);

    private static List<CubicSegment> BuildCustomSegments(IReadOnlyList<BezierPoint>? points)
    {
        var normalized = NormalizeBezierPoints(points);
        var segments = new List<CubicSegment>(normalized.Length - 1);
        for (int i = 0; i < normalized.Length - 1; i++)
        {
            segments.Add(new CubicSegment(
                normalized[i],
                (normalized[i].OutX, normalized[i].OutY),
                (normalized[i + 1].InX, normalized[i + 1].InY),
                normalized[i + 1]));
        }
        return segments;
    }

    private static (double X, double Y) CubicAt(double t, BezierPoint p0, (double X, double Y) c0, (double X, double Y) c1, BezierPoint p1)
    {
        double mt = 1 - t;
        double mt2 = mt * mt;
        double t2 = t * t;
        double w0 = mt2 * mt;
        double w1 = 3 * mt2 * t;
        double w2 = 3 * mt * t2;
        double w3 = t2 * t;
        return (
            X: w0 * p0.X + w1 * c0.X + w2 * c1.X + w3 * p1.X,
            Y: w0 * p0.Y + w1 * c0.Y + w2 * c1.Y + w3 * p1.Y);
    }

    private static double SolveBezierTForX(double x, CubicSegment seg)
    {
        double lo = 0, hi = 1;
        for (int i = 0; i < 28; i++)
        {
            double mid = (lo + hi) / 2;
            double xm = CubicAt(mid, seg.P0, seg.C0, seg.C1, seg.P1).X;
            if (xm < x) lo = mid;
            else hi = mid;
        }
        return (lo + hi) / 2;
    }

    public static double EvaluateCustomCurve(double x, IReadOnlyList<BezierPoint>? points)
    {
        var segments = BuildCustomSegments(points);
        if (segments.Count == 0) return x;

        var first = segments[0].P0;
        if (x <= first.X) return first.Y;

        var lastSegment = segments[^1];
        var lastPoint = lastSegment.P1;
        if (x >= lastPoint.X) return lastPoint.Y;

        foreach (var seg in segments)
        {
            double x0 = seg.P0.X;
            double x1 = seg.P1.X;
            if (x < x0 || x > x1) continue;

            double span = x1 - x0;
            if (span <= 1e-6) return seg.P1.Y;
            double t = SolveBezierTForX(x, seg);
            return CubicAt(t, seg.P0, seg.C0, seg.C1, seg.P1).Y;
        }

        return lastPoint.Y;
    }

    /// <summary>
    /// The mapping the brush actually obeys: curve 1, then curve 2 over its output.
    /// </summary>
    /// <remarks>
    /// This is what the effective chart draws. Composition is the whole feature — the two
    /// charts above it each show a half, and neither on its own says what the pen will do.
    /// </remarks>
    public static double ApplyPressureCurve(double x, PressureCurveParams p)
        => ApplyCurve(ApplyCurve(x, p.Curve1), p.Curve2);

    /// <summary>Applies one curve. The unit both stages are built from.</summary>
    public static double ApplyCurve(double x, CurveSettings p)
    {
        if (p.CurveType == CurveType.Passthrough) return x;
        if (p.CurveType == CurveType.Flat) return p.FlatLevel;
        // Inverted has no settings of its own: hard pressure gives a light stroke and
        // vice versa. Like Passthrough and Flat it never reaches RawCurveOutput, which
        // covers only the parametric power-law/sigmoid family.
        if (p.CurveType == CurveType.Inverted) return 1 - Clamp01(x);
        if (p.CurveType == CurveType.Bezier)
        {
            double clampedX = Clamp01(x);
            return EvaluateCustomCurve(clampedX, p.BezierPoints);
        }

        // Basic exposes no range or min-approach controls, so it must not silently apply
        // values left over from Extended/Sigmoid or carried in a preset. See
        // UsesRangeControls.
        bool usesRange = UsesRangeControls(p.CurveType);
        double inMin = usesRange ? p.InputMinimum : 0;
        double inMax = usesRange ? p.InputMaximum : 1;

        if (usesRange && p.MinApproach == MinApproach.Cut && x < inMin) return 0;

        double inputRange = inMax - inMin;
        double xNorm = inputRange > 0 ? Clamp01((x - inMin) / inputRange) : 0;
        return RawCurveOutput(xNorm, p);
    }

    private static double Clamp01(double v) => Math.Min(1, Math.Max(0, v));
}
