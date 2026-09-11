using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using PenDynamicsLab.Curves;

namespace PenDynamicsLab.Persistence;

/// <summary>
/// Reads both preset shapes: the current one, with <c>Curve1</c> and <c>Curve2</c>, and
/// the one written before a second curve existed, which had the curve's fields flat on
/// the object.
/// </summary>
/// <remarks>
/// <para>
/// A legacy preset loads as <b>curve 1, with curve 2 at Passthrough</b> — which is exactly
/// what its behaviour was, since a single curve and a curve followed by a bypass are the
/// same mapping. Nobody's saved preset changes meaning.
/// </para>
/// <para>
/// Writing is always the current shape. That makes the migration one-way on save: open a
/// preset in this version and it is rewritten with both curves. Since the legacy read is
/// lossless that costs nothing, and it keeps exactly one shape being produced.
/// </para>
/// <para>
/// Detection is by the presence of <c>Curve1</c>, not by a version number. A version field
/// would have had to exist before it was needed, which it did not.
/// </para>
/// </remarks>
public sealed class PressureCurveParamsConverter : JsonConverter<PressureCurveParams>
{
    /// <summary>Everything either shape can carry, all optional.</summary>
    private sealed record Dto
    {
        // Current shape.
        public int? QuantizationLevels { get; init; }
        public CurveSettings? Curve1 { get; init; }
        public CurveSettings? Curve2 { get; init; }

        // Shared by both shapes.
        public SmoothingType? SmoothingType { get; init; }
        public double? EmaSmoothing { get; init; }
        // Presets written before the order became an application setting still carry a
        // SmoothingOrder. System.Text.Json ignores members the DTO does not declare, so
        // it is dropped on read — which is the intent.

        // Legacy: one curve's fields, flat.
        public CurveType? CurveType { get; init; }
        public double? Softness { get; init; }
        public double? InputMinimum { get; init; }
        public double? InputMaximum { get; init; }
        public double? Minimum { get; init; }
        public double? Maximum { get; init; }
        public MinApproach? MinApproach { get; init; }
        public double? FlatLevel { get; init; }
        public ImmutableArray<BezierPoint>? BezierPoints { get; init; }
    }

    public override PressureCurveParams Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Deserialize without this converter, or it recurses.
        var inner = new JsonSerializerOptions(options);
        for (int i = inner.Converters.Count - 1; i >= 0; i--)
            if (inner.Converters[i] is PressureCurveParamsConverter)
                inner.Converters.RemoveAt(i);

        var dto = JsonSerializer.Deserialize<Dto>(ref reader, inner) ?? new Dto();
        var d = CurveSettings.Default;

        var curve1 = dto.Curve1 ?? new CurveSettings
        {
            CurveType = dto.CurveType ?? d.CurveType,
            Softness = dto.Softness ?? d.Softness,
            InputMinimum = dto.InputMinimum ?? d.InputMinimum,
            InputMaximum = dto.InputMaximum ?? d.InputMaximum,
            Minimum = dto.Minimum ?? d.Minimum,
            Maximum = dto.Maximum ?? d.Maximum,
            MinApproach = dto.MinApproach ?? d.MinApproach,
            FlatLevel = dto.FlatLevel ?? d.FlatLevel,
            BezierPoints = dto.BezierPoints ?? d.BezierPoints,
        };

        return new PressureCurveParams
        {
            // Absent in every preset saved before quantization existed, which is exactly
            // right: those presets did not quantize.
            QuantizationLevels = dto.QuantizationLevels ?? 0,
            Curve1 = curve1,
            Curve2 = dto.Curve2 ?? CurveSettings.Default,
            SmoothingType = dto.SmoothingType ?? PressureCurveParams.Default.SmoothingType,
            EmaSmoothing = dto.EmaSmoothing ?? PressureCurveParams.Default.EmaSmoothing,
        };
    }

    public override void Write(Utf8JsonWriter writer, PressureCurveParams value, JsonSerializerOptions options)
    {
        var inner = new JsonSerializerOptions(options);
        for (int i = inner.Converters.Count - 1; i >= 0; i--)
            if (inner.Converters[i] is PressureCurveParamsConverter)
                inner.Converters.RemoveAt(i);

        JsonSerializer.Serialize(writer, value, inner);
    }
}
