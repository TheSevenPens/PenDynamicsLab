using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PenDynamicsLab.Persistence;

/// <summary>
/// One row of pen-response measurement data: physical force in grams-force and the
/// resulting logical pressure percentage (0-100) reported by the tablet driver.
/// </summary>
public sealed record ResponseRecord(double Gf, double LogicalPercent);

/// <summary>
/// A pressure-response dataset for a specific pen / tablet combination, matching the
/// JSON schema bundled with WebPressureExplorer's sample data.
/// </summary>
public sealed record PressureResponseData(
    string Brand,
    string Pen,
    string PenFamily,
    string InventoryId,
    string Date,
    string User,
    string Tablet,
    string Driver,
    string Os,
    string Notes,
    IReadOnlyList<ResponseRecord> Records);

public static class PressureResponseLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ResponseRecordConverter() },
    };

    public sealed record SampleEntry(string Label, string ResourceName);

    /// <summary>Built-in WACOM KP-504E samples, embedded as assembly resources.</summary>
    public static readonly IReadOnlyList<SampleEntry> Samples =
    [
        new SampleEntry("WAP.0038 — KP-504E (unit 1)", "PenDynamicsLab.Persistence.SampleResponses.WAP_0038_2025_11_10.json"),
        new SampleEntry("WAP.0047 — KP-504E (unit 2)", "PenDynamicsLab.Persistence.SampleResponses.WAP_0047_2025_11_10.json"),
        new SampleEntry("WAP.0050 — KP-504E (unit 3)", "PenDynamicsLab.Persistence.SampleResponses.WAP_0050_2025_11_10.json"),
    ];

    public static PressureResponseData LoadSample(string resourceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static PressureResponseData LoadFromFile(string path)
        => Parse(File.ReadAllText(path));

    public static PressureResponseData Parse(string json)
    {
        var raw = JsonSerializer.Deserialize<RawJson>(json, JsonOptions)
            ?? throw new InvalidDataException("Empty JSON.");
        if (raw.Records is null || raw.Records.Count == 0)
            throw new InvalidDataException("Missing or empty 'records' array.");
        return new PressureResponseData(
            Brand: raw.Brand ?? "",
            Pen: raw.Pen ?? "",
            PenFamily: raw.Penfamily ?? "",
            InventoryId: raw.Inventoryid ?? "",
            Date: raw.Date ?? "",
            User: raw.User ?? "",
            Tablet: raw.Tablet ?? "",
            Driver: raw.Driver ?? "",
            Os: raw.Os ?? "",
            Notes: raw.Notes ?? "",
            Records: raw.Records);
    }

    private sealed class RawJson
    {
        public string? Brand { get; set; }
        public string? Pen { get; set; }
        public string? Penfamily { get; set; }
        public string? Inventoryid { get; set; }
        public string? Date { get; set; }
        public string? User { get; set; }
        public string? Tablet { get; set; }
        public string? Driver { get; set; }
        public string? Os { get; set; }
        public string? Notes { get; set; }
        public List<ResponseRecord>? Records { get; set; }
    }

    /// <summary>Reads a 2-element JSON array as <see cref="ResponseRecord"/>.</summary>
    private sealed class ResponseRecordConverter : JsonConverter<ResponseRecord>
    {
        public override ResponseRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected array for ResponseRecord");
            reader.Read();
            double gf = reader.GetDouble();
            reader.Read();
            double pct = reader.GetDouble();
            reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException("Expected end of 2-element array");
            return new ResponseRecord(gf, pct);
        }

        public override void Write(Utf8JsonWriter writer, ResponseRecord value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.Gf);
            writer.WriteNumberValue(value.LogicalPercent);
            writer.WriteEndArray();
        }
    }
}
