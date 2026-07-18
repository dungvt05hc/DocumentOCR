using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.OcrBenchmark;

/// <summary>
/// Runs one OCR provider against one sample file, pushes the result through the same
/// extraction/normalization/validation pipeline <c>DocumentProcessingService</c> uses (minus
/// storage/DB), and writes the four debug JSON artifacts plus a CSV summary row.
/// </summary>
internal sealed class BenchmarkFileProcessor(
    IFieldExtractionService extraction,
    IFieldNormalizationService normalization,
    IFieldValidationService validation)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // ExtractedField/ValidationWarning carry a non-nullable `Document` nav property that
        // stays null here (never hydrated outside EF) — drop it instead of writing "Document": null.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<BenchmarkCsvRow> ProcessAsync(
        IDocumentOcrProvider provider,
        Guid documentId,
        byte[] fileBytes,
        string fileName,
        string contentType,
        string outputDir,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputDir);

        OcrResult ocrResult;
        try
        {
            await using var content = new MemoryStream(fileBytes, writable: false);
            var input = new DocumentInput
            {
                Content = content,
                FileName = fileName,
                ContentType = contentType,
                FileSizeBytes = fileBytes.Length
            };

            ocrResult = await provider.AnalyzeAsync(input, ct);
        }
        catch (Exception ex)
        {
            // A provider throwing (vs. returning Success=false) must not abort the whole batch.
            await File.WriteAllTextAsync(
                Path.Combine(outputDir, "ocr-result.json"),
                $$"""{ "success": false, "errorMessage": {{JsonSerializer.Serialize(ex.Message)}} }""",
                ct);

            return new BenchmarkCsvRow(
                fileName, provider.ProviderName, ModelId: null, ProcessingDurationMs: 0,
                PageCount: 0, FullTextLength: 0, AverageConfidence: null,
                SupplierTaxCode: null, InvoiceDate: null, TotalAmount: null, WarningCount: 0,
                ErrorMessage: ex.Message);
        }

        var fields = extraction.Extract(documentId, ocrResult);
        normalization.NormalizeFields(fields);
        var warnings = validation.Validate(documentId, fields);

        await WriteRawResponseAsync(outputDir, ocrResult, ct);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "ocr-result.json"),
            JsonSerializer.Serialize(ocrResult, JsonOptions), ct);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "extracted-fields.json"),
            JsonSerializer.Serialize(fields, JsonOptions), ct);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "validation-warnings.json"),
            JsonSerializer.Serialize(warnings, JsonOptions), ct);

        return new BenchmarkCsvRow(
            FileName: fileName,
            ProviderName: provider.ProviderName,
            ModelId: ocrResult.ModelId,
            ProcessingDurationMs: ocrResult.ProcessingTimeMs,
            PageCount: ocrResult.PageCount,
            FullTextLength: ocrResult.FullText.Length,
            AverageConfidence: AverageFieldConfidence(fields),
            SupplierTaxCode: FieldValue(fields, FieldName.SupplierTaxCode),
            InvoiceDate: FieldValue(fields, FieldName.InvoiceDate),
            TotalAmount: FieldValue(fields, FieldName.TotalAmount),
            WarningCount: warnings.Count,
            ErrorMessage: ocrResult.Success ? null : ocrResult.ErrorMessage);
    }

    // ── AverageConfidence: mean confidence across the extracted canonical fields (SupplierName,
    // SupplierTaxCode, InvoiceNumber, ... TotalAmount) — this reflects field-extraction quality,
    // which is what this benchmark compares between providers. OcrResult.Confidence is not used
    // because AzureDocumentIntelligenceProvider does not currently populate it. ──────────────────
    private static double? AverageFieldConfidence(IReadOnlyList<ExtractedField> fields)
    {
        var confidences = fields.Where(f => f.Confidence.HasValue).Select(f => f.Confidence!.Value).ToList();
        return confidences.Count > 0 ? confidences.Average() : null;
    }

    private static string? FieldValue(IReadOnlyList<ExtractedField> fields, FieldName fieldName)
    {
        var field = fields.FirstOrDefault(f => f.FieldName == fieldName.ToString());
        if (field is null) return null;
        return !string.IsNullOrWhiteSpace(field.NormalizedValue) ? field.NormalizedValue : field.RawValue;
    }

    private static async Task WriteRawResponseAsync(string outputDir, OcrResult ocrResult, CancellationToken ct)
    {
        var path = Path.Combine(outputDir, "raw-response.json");

        if (ocrResult.RawProviderResponseJson is null)
        {
            await File.WriteAllTextAsync(
                path,
                """{ "note": "This provider did not produce a raw response (e.g. FakeOcrProvider, or the request failed before Azure responded)." }""",
                ct);
            return;
        }

        // RawProviderResponseJson is already a JSON string (Azure's raw HTTP response body) —
        // parse and re-serialize indented for readability rather than double-encoding it.
        try
        {
            using var doc = JsonDocument.Parse(ocrResult.RawProviderResponseJson);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(doc.RootElement, JsonOptions), ct);
        }
        catch (JsonException)
        {
            await File.WriteAllTextAsync(path, ocrResult.RawProviderResponseJson, ct);
        }
    }
}
