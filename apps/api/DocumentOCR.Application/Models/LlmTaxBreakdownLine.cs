using System.Text.Json.Serialization;

namespace DocumentOCR.Application.Models;

/// <summary>One VAT-rate line as reported by <see cref="Interfaces.ILlmExtractionClient"/> (thuế suất / tiền chưa thuế / tiền thuế).</summary>
public sealed record LlmTaxBreakdownLine(
    [property: JsonPropertyName("thueSuat")] LlmFieldValue? ThueSuat,
    [property: JsonPropertyName("tienChuaThue")] LlmFieldValue? TienChuaThue,
    [property: JsonPropertyName("tienThue")] LlmFieldValue? TienThue);
