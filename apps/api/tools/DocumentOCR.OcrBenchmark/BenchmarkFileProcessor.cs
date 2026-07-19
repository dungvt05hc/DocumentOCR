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

        NormalizedOcrDocument ocrResult;
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
                fileName, DocumentCategory: null, provider.ProviderName, ModelId: null, Features: "",
                ProcessingDurationMs: 0, PageCount: 0, FullTextLength: 0, LineCount: 0, WordCount: 0,
                ParagraphCount: 0, TableCount: 0, KeyValuePairCount: 0, AverageConfidence: null,
                ExtractedSupplierName: null, ExtractedSupplierTaxCode: null, ExtractedInvoiceNumber: null,
                ExtractedInvoiceDate: null, ExtractedSubtotalAmount: null, ExtractedVatAmount: null,
                ExtractedTotalAmount: null, ExtractedCurrency: null, WarningCount: 0,
                RawProviderResponsePath: null, NormalizedOcrResultPath: null, ErrorMessage: ex.Message);
        }

        var fields = extraction.Extract(documentId, ocrResult);
        normalization.NormalizeFields(fields);
        var warnings = validation.Validate(documentId, fields);

        var rawResponsePath = await WriteRawResponseAsync(outputDir, ocrResult, ct);

        var ocrResultPath = Path.Combine(outputDir, "ocr-result.json");
        await File.WriteAllTextAsync(ocrResultPath, JsonSerializer.Serialize(ocrResult, JsonOptions), ct);

        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "extracted-fields.json"),
            JsonSerializer.Serialize(fields, JsonOptions), ct);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "validation-warnings.json"),
            JsonSerializer.Serialize(warnings, JsonOptions), ct);

        return new BenchmarkCsvRow(
            FileName: fileName,
            DocumentCategory: FieldValue(fields, FieldName.DocumentType),
            ProviderName: provider.ProviderName,
            ModelId: ocrResult.ModelId,
            Features: string.Join('|', ocrResult.Features),
            ProcessingDurationMs: ocrResult.ProcessingTimeMs,
            PageCount: ocrResult.PageCount,
            FullTextLength: ocrResult.FullText.Length,
            LineCount: ocrResult.Lines.Count,
            WordCount: ocrResult.Words.Count,
            ParagraphCount: ocrResult.Paragraphs.Count,
            TableCount: ocrResult.Tables.Count,
            KeyValuePairCount: ocrResult.KeyValuePairs.Count,
            AverageConfidence: AverageFieldConfidence(fields),
            ExtractedSupplierName: FieldValue(fields, FieldName.SupplierName),
            ExtractedSupplierTaxCode: FieldValue(fields, FieldName.SupplierTaxCode),
            ExtractedInvoiceNumber: FieldValue(fields, FieldName.InvoiceNumber),
            ExtractedInvoiceDate: FieldValue(fields, FieldName.InvoiceDate),
            ExtractedSubtotalAmount: FieldValue(fields, FieldName.SubtotalAmount),
            ExtractedVatAmount: FieldValue(fields, FieldName.VatAmount),
            ExtractedTotalAmount: FieldValue(fields, FieldName.TotalAmount),
            ExtractedCurrency: FieldValue(fields, FieldName.Currency),
            WarningCount: warnings.Count,
            RawProviderResponsePath: rawResponsePath,
            NormalizedOcrResultPath: ocrResultPath,
            ErrorMessage: ocrResult.Success ? null : ocrResult.ErrorMessage);
    }

    // ── AverageConfidence: mean confidence across the extracted canonical fields (SupplierName,
    // SupplierTaxCode, InvoiceNumber, ... TotalAmount) — this reflects field-extraction quality,
    // which is what this benchmark compares between providers. NormalizedOcrDocument.AverageConfidence
    // is not used because AzureDocumentIntelligenceProvider derives it from word confidences, which
    // isn't directly comparable to FakeOcrProvider's flat document-level value. ──────────────────
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

    private static async Task<string?> WriteRawResponseAsync(string outputDir, NormalizedOcrDocument ocrResult, CancellationToken ct)
    {
        var path = Path.Combine(outputDir, "raw-response.json");

        if (ocrResult.RawProviderResponseJson is null)
        {
            await File.WriteAllTextAsync(
                path,
                """{ "note": "This provider did not produce a raw response (e.g. FakeOcrProvider, or the request failed before Azure responded)." }""",
                ct);
            return null;
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

        return path;
    }
}
