using System.Collections.Immutable;

namespace PenDynamicsLab.Curves;

public static class CurveMath
{
    public static double CubicHermite(double t, double y0, double m0, double y1, double m1)
    {
        double t2 = t * t;
        double t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * y0
             + (t3 - 2 * t2 + t) * m0
             + (-2 * t3 + 3 * t2) * y1
             + (t3 - t2) * m1;
    }

    public static double RawCurveOutput(double xNorm, PressureCurveParams p)
    {
        double curved;

        if (p.CurveType == CurveType.Sigmoid)
        {
            double k = p.Softness * 14;
            if (Math.Abs(k) < 0.01)
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

        return p.Minimum + curved * (p.Maximum - p.Minimum);

        static double Sig(double t, double k) => 1.0 / (1.0 + Math.Exp(-k * (t - 0.5)));
    }

    public static double RawCurveSlope(double xNorm, PressureCurveParams p)
    {
        const double epsilon = 0.0005;
        double x1 = Math.Min(1, xNorm + epsilon);
        double x0 = Math.Max(0, xNorm - epsilon);
        return (RawCurveOutput(x1, p) - RawCurveOutput(x0, p)) / (x1 - x0);
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

    public static double ApplyPressureCurve(double x, PressureCurveParams p)
    {
        if (p.CurveType == CurveType.Passthrough) return x;
        if (p.CurveType == CurveType.Flat) return p.FlatLevel;
        if (p.CurveType == CurveType.Bezier)
        {
            double clampedX = Clamp01(x);
            return EvaluateCustomCurve(clampedX, p.BezierPoints);
        }

        if (p.MinApproach == MinApproach.Cut && x < p.InputMinimum) return 0;

        double inputRange = p.InputMaximum - p.InputMinimum;
        double xNorm = inputRange > 0 ? Clamp01((x - p.InputMinimum) / inputRange) : 0;
        double baseOutput = RawCurveOutput(xNorm, p);

        if (p.TransitionWidth > 0)
        {
            double tw = p.TransitionWidth;

            if (xNorm < tw)
            {
                double t = xNorm / tw;
                double y1 = RawCurveOutput(tw, p);
                double m1 = RawCurveSlope(tw, p) * tw;
                return CubicHermite(t, p.Minimum, 0, y1, m1);
            }

            if (xNorm > 1 - tw)
            {
                double s = (xNorm - (1 - tw)) / tw;
                double y0 = RawCurveOutput(1 - tw, p);
                double m0 = RawCurveSlope(1 - tw, p) * tw;
                return CubicHermite(s, y0, m0, p.Maximum, 0);
            }
        }

        return baseOutput;
    }

    private static double Clamp01(double v) => Math.Min(1, Math.Max(0, v));
}
