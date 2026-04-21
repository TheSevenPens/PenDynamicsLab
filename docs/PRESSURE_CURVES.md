# Pressure Curves

This document details the pressure curve types available in PenDynamicsLab, their settings, and the math behind each. The math is a verbatim port of WebPressureExplorer's `curveMath.js` — pinned by the xUnit tests in `PenDynamicsLab.Tests/CurveMathTests.cs`.

## Overview

A pressure curve is a function `f(x) → y` where:
- **x** = raw pen pressure input (0 to 1)
- **y** = transformed output pressure (0 to 1)

The output drives brush size, opacity, or other pressure-dependent parameters. Different curve shapes give different drawing "feels" — a concave curve makes light strokes more sensitive, while a convex curve requires more force for the same effect.

In code, the entry point is `CurveMath.ApplyPressureCurve(double x, PressureCurveParams p)`.

## Common settings

These settings apply to **basic**, **extended**, and **sigmoid** curve types:

| Setting | Range | Description |
|---|---|---|
| `InputMinimum` | 0-1 | Below this input value, output is clamped (or cut to zero). Controlled by the pink min control node's X position. |
| `InputMaximum` | 0-1 | Above this input value, output is clamped to the maximum. Controlled by the cyan max control node's X position. |
| `Minimum` | 0-1 | The output value at the min control node. Controlled by the pink node's Y position. |
| `Maximum` | 0-1 | The output value at the max control node. Controlled by the cyan node's Y position. |
| `Softness` | -0.9 to 0.9 | Controls the curve shape. Positive = concave (lighter feel), negative = convex (heavier feel), zero = linear. |
| `MinApproach` | `Clamp` / `Cut` | How the curve behaves below `InputMinimum` (see below). |
| `TransitionWidth` | 0-0.5 | Cubic Hermite smoothing at the boundaries. |

Note: PenDynamicsLab shows the min/max nodes for **Sigmoid** and **Extended** curves only. **Basic** intentionally hides them — same convention as WebPressureExplorer — so Basic effectively uses the full [0, 1] input/output range.

### Min approach modes

Controls the curve segment from `x = 0` to `x = InputMinimum`:

- **Clamp** (default): Output holds at `Minimum` across the entire range. The curve is `(0, Minimum) → (InputMinimum, Minimum)`.
- **Cut**: Output is zero until `InputMinimum`, then jumps up. The curve is `(0, 0) → (InputMinimum, 0) → (InputMinimum, Minimum)`.

## Curve types

### Passthrough

Output equals input: `f(x) = x`. Draws a straight diagonal line from (0, 0) to (1, 1). No settings apply. Useful as a baseline to see raw pen behavior.

### Flat

Constant output: `f(x) = FlatLevel` for all inputs.

| Setting | Range | Description |
|---|---|---|
| `FlatLevel` | 0-1 | The constant output value |

Draws a horizontal line. Every input pressure produces the same output.

### Basic

Power curve. The core curve shape is a power function applied to the normalized input. Min/max nodes are not shown — the curve runs across the full input/output range.

#### Math

1. **Normalize** the input to the [`InputMinimum`, `InputMaximum`] range:
   ```
   xNorm = clamp((x - inputMinimum) / (inputMaximum - inputMinimum), 0, 1)
   ```

2. **Compute exponent** from `Softness`:
   ```
   if softness >= 0:
     exponent = 1 - softness
   else:
     exponent = 1 / (1 + softness)
   ```
   - softness = 0 → exponent = 1 (linear)
   - softness = 0.5 → exponent = 0.5 (square root, concave)
   - softness = -0.5 → exponent = 2 (quadratic, convex)

3. **Apply power law:**
   ```
   curved = xNorm ^ exponent
   ```

4. **Scale to output range:**
   ```
   output = minimum + curved * (maximum - minimum)
   ```

#### Behavior
- **softness > 0** (concave): Light pressure is more sensitive. Curve rises steeply, then flattens. Good for detail work.
- **softness < 0** (convex): Light pressure is less sensitive. Curve starts flat, then rises steeply. Gives more control in the light-pressure range.
- **softness = 0** (linear): Straight line from min to max.

### Extended

Same math as **Basic**, but exposes the full set of controls:
- Draggable pink min node (`InputMinimum`, `Minimum`)
- Draggable cyan max node (`InputMaximum`, `Maximum`)
- `MinApproach` toggle (Clamp / Cut)
- `TransitionWidth` for boundary smoothing

Use Extended when you want to remap input or output ranges; use Basic when you just want a power curve across [0, 1].

### Sigmoid

S-curve via the logistic function. Compresses both the light and heavy ends while expanding the midrange.

#### Math

1. **Normalize** the input (same as basic).

2. **Compute steepness:**
   ```
   k = softness * 14
   ```
   If `|k| < 0.01`, falls back to linear (`curved = xNorm`).

3. **Apply logistic function:**
   ```
   sig(t) = 1 / (1 + exp(-k * (t - 0.5)))
   s0 = sig(0)
   s1 = sig(1)
   curved = (sig(xNorm) - s0) / (s1 - s0)
   ```
   The normalization by `s0` and `s1` ensures the output maps cleanly to [0, 1].

4. **Scale to output range** (same as basic).

#### Behavior
- **softness > 0**: Standard S-curve. Light and heavy pressure are compressed; midrange is expanded. The higher the softness, the steeper the S.
- **softness < 0**: Inverted S-curve. Midrange is compressed; extremes are expanded.
- **softness = 0**: Linear (same as basic at softness = 0).

### Bezier

Custom cubic bezier curve. Users define 2-16 anchor points with adjustable control handles. Provides complete freedom to shape the pressure response.

#### Points

Each `BezierPoint` has:
- `(X, Y)` — anchor position on the curve
- `(InX, InY)` — incoming control handle (influences the curve arriving at this point)
- `(OutX, OutY)` — outgoing control handle (influences the curve leaving this point)
- `HandleMode` — `Broken` (handles move independently) or `Mirrored` (handles are symmetric around the anchor)

Points are sorted by X. The first point is pinned to `X = 0` and the last to `X = 1`. Endpoint handles are forced to their anchor position.

Built-in presets are defined in `Curves/BezierPresets.cs`: Linear, Soft, Firm, S-Curve, Light Touch, Heavy, Step.

#### Math

Between each pair of adjacent anchor points, a cubic bezier segment is defined:
- **P0** = current anchor `(X, Y)`
- **C0** = current anchor's outgoing handle `(OutX, OutY)`
- **C1** = next anchor's incoming handle `(InX, InY)`
- **P1** = next anchor `(X, Y)`

The cubic bezier formula:
```
B(t) = (1-t)^3 * P0 + 3*(1-t)^2*t * C0 + 3*(1-t)*t^2 * C1 + t^3 * P1
```

To evaluate the curve at a given input x:
1. Find the segment where `P0.x ≤ x ≤ P1.x`
2. Binary search (28 iterations) for the parameter `t` where `B(t).x = x`
3. Return `B(t).y`

The 28-iteration binary search gives roughly 3.7 × 10⁻⁹ precision. This is why the `Apply_BezierLinearPreset_IsApproximatelyIdentity` test uses a tolerance of 1 × 10⁻⁷ rather than checking for exact identity.

#### Normalization

`CurveMath.NormalizeBezierPoints` is called on every change:
- Values clamped to [0, 1]
- Sorted by X
- First point forced to `X = 0`, last to `X = 1`
- Missing endpoints auto-generated
- Handle X values constrained to stay within adjacent anchor X bounds
- Endpoint handles forced to their anchor position (no overshoot)

## Boundary transition smoothing

When `TransitionWidth > 0`, cubic Hermite interpolation smooths the transition at the boundaries of the input range to avoid sharp corners where the flat clamped segments meet the curve.

```
cubicHermite(t, y0, m0, y1, m1) =
  (2t^3 - 3t^2 + 1)*y0 + (t^3 - 2t^2 + t)*m0 +
  (-2t^3 + 3t^2)*y1   + (t^3 - t^2)*m1
```

Applied at both the minimum boundary (`xNorm < TransitionWidth`) and maximum boundary (`xNorm > 1 - TransitionWidth`), blending between the clamped flat value and the curve with matching slope continuity.

## Pressure processing pipeline

The full pipeline from raw pen input to final stroke parameter (per pen point inside `MainWindow.RenderTimer_Tick`):

```
Raw pen pressure (pt.Pressure / pt.MaxPressure  → 0..1)
  │
  ▼
[EMA smoothing]    ← if SmoothingOrder = SmoothThenCurve
  │
  ▼
[ApplyPressureCurve]    ← curve type + all settings
  │
  ▼
[EMA smoothing]    ← if SmoothingOrder = CurveThenSmooth
  │
  ▼
Output pressure (0..1)  →  brush size  OR  opacity  (per PressureControl)
```

EMA (Exponential Moving Average) smoothing:
```
smoothed = smoothed + alpha * (raw - smoothed)
alpha    = 1 - emaSmoothing
```

When `EmaSmoothing = 0`, alpha = 1, so output = input (no smoothing). As it approaches 0.99, output becomes increasingly smoothed/lagged. The same formula is used for position smoothing on the (x, y) canvas-local coordinate.

The "live" indicators on the curve and response charts use:
- **Raw** (purple) = the unprocessed `pt.Pressure / pt.MaxPressure`
- **Effective** (green) = the value entering `ApplyPressureCurve` (smoothed in `SmoothThenCurve` mode, raw in `CurveThenSmooth` mode)
