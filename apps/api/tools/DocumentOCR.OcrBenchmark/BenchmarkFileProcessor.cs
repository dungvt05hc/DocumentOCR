using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.OcrBenchmark;

/// <summary>
/// Runs one OCR provider (or, for PDF text-layer+LLM, one <see cref="IDocumentExtractionStrategy"/>)
/// against one sample file, pushes the result through the same normalization/validation pipeline
/// <c>DocumentProcessingService</c> uses (minus storage/DB), and writes debug JSON artifacts plus a
/// CSV summary row. <see cref="ProcessAsync"/> and <see cref="ProcessStrategyAsync"/> share the
/// ground-truth-matching/row-building tail via <see cref="BuildRow"/> — only how fields get
/// extracted differs (heuristic <c>FieldExtractionService</c> over a raw OCR result vs. a
/// strategy's own already-mapped fields).
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
        CancellationToken ct,
        GroundTruthRow? groundTruth = null)
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
            await WriteFailureArtifactAsync(outputDir, "ocr-result.json", ex.Message, ct);
            return BuildFailureRow(fileName, provider.ProviderName, groundTruth, ex.Message);
        }

        var fields = extraction.Extract(documentId, ocrResult);
        normalization.NormalizeFields(fields);
        var warnings = validation.Validate(documentId, fields);

        var rawResponsePath = await WriteRawResponseAsync(outputDir, ocrResult, ct);

        var ocrResultPath = Path.Combine(outputDir, "ocr-result.json");
        await File.WriteAllTextAsync(ocrResultPath, JsonSerializer.Serialize(ocrResult, JsonOptions), ct);
        await WriteFieldsAndWarningsAsync(outputDir, fields, warnings, ct);

        return BuildRow(
            fileName: fileName,
            providerName: provider.ProviderName,
            modelId: ocrResult.ModelId,
            features: string.Join('|', ocrResult.Features),
            processingDurationMs: ocrResult.ProcessingTimeMs,
            pageCount: ocrResult.PageCount,
            fullTextLength: ocrResult.FullText.Length,
            lineCount: ocrResult.Lines.Count,
            wordCount: ocrResult.Words.Count,
            paragraphCount: ocrResult.Paragraphs.Count,
            tableCount: ocrResult.Tables.Count,
            keyValuePairCount: ocrResult.KeyValuePairs.Count,
            estimatedCost: ocrResult.EstimatedCost,
            rejectedFieldCount: 0,
            fields: fields,
            warningCount: warnings.Count,
            groundTruth: groundTruth,
            rawResponsePath: rawResponsePath,
            normalizedResultPath: ocrResultPath,
            errorMessage: ocrResult.Success ? null : ocrResult.ErrorMessage);
    }

    /// <summary>
    /// Same shape as <see cref="ProcessAsync"/> but for an <see cref="IDocumentExtractionStrategy"/>
    /// (currently only used for the PDF text-layer+LLM strategy) — the strategy already returns
    /// mapped <see cref="ExtractedField"/>s (with confidence/verification already applied), so
    /// there's no <see cref="IFieldExtractionService.Extract"/> step here.
    /// </summary>
    public async Task<BenchmarkCsvRow> ProcessStrategyAsync(
        IDocumentExtractionStrategy strategy,
        Guid documentId,
        byte[] fileBytes,
        string fileName,
        string contentType,
        string outputDir,
        CancellationToken ct,
        GroundTruthRow? groundTruth = null)
    {
        Directory.CreateDirectory(outputDir);

        StructuredExtractionResult result;
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

            result = await strategy.ExtractAsync(input, ct);
        }
        catch (Exception ex)
        {
            await WriteFailureArtifactAsync(outputDir, "extraction-result.json", ex.Message, ct);
            return BuildFailureRow(fileName, strategy.Name, groundTruth, ex.Message);
        }

        var fields = result.Fields.ToList();
        foreach (var field in fields)
            field.DocumentId = documentId;

        // Idempotent safety net matching DocumentProcessingService's own shared normalize step —
        // PdfTextLayerLlmStrategy already pre-fills NormalizedValue for the fields it verifies.
        normalization.NormalizeFields(fields);
        var warnings = validation.Validate(documentId, fields);

        await WriteFieldsAndWarningsAsync(outputDir, fields, warnings, ct);
        var resultPath = Path.Combine(outputDir, "extraction-result.json");
        await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, JsonOptions), ct);

        return BuildRow(
            fileName: fileName,
            providerName: result.ProviderName,
            modelId: result.ModelId,
            features: "",
            processingDurationMs: result.ProcessingTimeMs,
            pageCount: result.PageCount,
            fullTextLength: result.RawSourceText?.Length ?? 0,
            lineCount: 0,
            wordCount: 0,
            paragraphCount: 0,
            tableCount: 0,
            keyValuePairCount: 0,
            estimatedCost: result.EstimatedCost,
            rejectedFieldCount: result.RejectedFieldCount,
            fields: fields,
            warningCount: warnings.Count,
            groundTruth: groundTruth,
            rawResponsePath: null,
            normalizedResultPath: resultPath,
            errorMessage: result.Success ? null : result.ErrorMessage);
    }

    // ── Shared ground-truth matching + row building ─────────────────────────────

    private static BenchmarkCsvRow BuildRow(
        string fileName,
        string providerName,
        string? modelId,
        string features,
        double processingDurationMs,
        int pageCount,
        int fullTextLength,
        int lineCount,
        int wordCount,
        int paragraphCount,
        int tableCount,
        int keyValuePairCount,
        decimal estimatedCost,
        int rejectedFieldCount,
        IReadOnlyList<ExtractedField> fields,
        int warningCount,
        GroundTruthRow? groundTruth,
        string? rawResponsePath,
        string? normalizedResultPath,
        string? errorMessage)
    {
        var extractedSupplierName = FieldValue(fields, FieldName.SupplierName);
        var extractedSupplierTaxCode = FieldValue(fields, FieldName.SupplierTaxCode);
        var extractedInvoiceNumber = FieldValue(fields, FieldName.InvoiceNumber);
        var extractedInvoiceDate = FieldValue(fields, FieldName.InvoiceDate);
        var extractedSubtotalAmount = FieldValue(fields, FieldName.SubtotalAmount);
        var extractedVatAmount = FieldValue(fields, FieldName.VatAmount);
        var extractedTotalAmount = FieldValue(fields, FieldName.TotalAmount);
        var extractedCurrency = FieldValue(fields, FieldName.Currency);

        var supplierNameMatched = GroundTruthComparer.MatchText(groundTruth?.ExpectedSupplierName, extractedSupplierName);
        var taxCodeMatched = GroundTruthComparer.MatchTaxCode(groundTruth?.ExpectedSupplierTaxCode, extractedSupplierTaxCode);
        var invoiceNumberMatched = GroundTruthComparer.MatchText(groundTruth?.ExpectedInvoiceNumber, extractedInvoiceNumber);
        var invoiceDateMatched = GroundTruthComparer.MatchDate(groundTruth?.ExpectedInvoiceDate, extractedInvoiceDate);
        var subtotalMatched = GroundTruthComparer.MatchMoney(groundTruth?.ExpectedSubtotalAmount, extractedSubtotalAmount);
        var vatMatched = GroundTruthComparer.MatchMoney(groundTruth?.ExpectedVatAmount, extractedVatAmount);
        var totalMatched = GroundTruthComparer.MatchMoney(groundTruth?.ExpectedTotalAmount, extractedTotalAmount);
        var currencyMatched = GroundTruthComparer.MatchCurrency(groundTruth?.ExpectedCurrency, extractedCurrency);

        return new BenchmarkCsvRow(
            FileName: fileName,
            DocumentCategory: FieldValue(fields, FieldName.DocumentType),
            ProviderName: providerName,
            ModelId: modelId,
            Features: features,
            ProcessingDurationMs: processingDurationMs,
            PageCount: pageCount,
            FullTextLength: fullTextLength,
            LineCount: lineCount,
            WordCount: wordCount,
            ParagraphCount: paragraphCount,
            TableCount: tableCount,
            KeyValuePairCount: keyValuePairCount,
            AverageConfidence: AverageFieldConfidence(fields),
            EstimatedCost: estimatedCost,
            RejectedFieldCount: rejectedFieldCount,
            ExtractedSupplierName: extractedSupplierName,
            ExpectedSupplierName: groundTruth?.ExpectedSupplierName,
            SupplierNameMatched: supplierNameMatched,
            ExtractedSupplierTaxCode: extractedSupplierTaxCode,
            ExpectedSupplierTaxCode: groundTruth?.ExpectedSupplierTaxCode,
            TaxCodeMatched: taxCodeMatched,
            ExtractedInvoiceNumber: extractedInvoiceNumber,
            ExpectedInvoiceNumber: groundTruth?.ExpectedInvoiceNumber,
            InvoiceNumberMatched: invoiceNumberMatched,
            ExtractedInvoiceDate: extractedInvoiceDate,
            ExpectedInvoiceDate: groundTruth?.ExpectedInvoiceDate,
            InvoiceDateMatched: invoiceDateMatched,
            ExtractedSubtotalAmount: extractedSubtotalAmount,
            ExpectedSubtotalAmount: groundTruth?.ExpectedSubtotalAmount,
            SubtotalMatched: subtotalMatched,
            ExtractedVatAmount: extractedVatAmount,
            ExpectedVatAmount: groundTruth?.ExpectedVatAmount,
            VatMatched: vatMatched,
            ExtractedTotalAmount: extractedTotalAmount,
            ExpectedTotalAmount: groundTruth?.ExpectedTotalAmount,
            TotalMatched: totalMatched,
            ExtractedCurrency: extractedCurrency,
            ExpectedCurrency: groundTruth?.ExpectedCurrency,
            CurrencyMatched: currencyMatched,
            FieldAccuracyPercent: GroundTruthComparer.CalculateFieldAccuracyPercent(
                supplierNameMatched, taxCodeMatched, invoiceNumberMatched, invoiceDateMatched,
                subtotalMatched, vatMatched, totalMatched, currencyMatched),
            WarningCount: warningCount,
            RawProviderResponsePath: rawResponsePath,
            NormalizedOcrResultPath: normalizedResultPath,
            ErrorMessage: errorMessage);
    }

    private static BenchmarkCsvRow BuildFailureRow(
        string fileName, string providerName, GroundTruthRow? groundTruth, string errorMessage) => new(
        fileName, DocumentCategory: null, providerName, ModelId: null, Features: "",
        ProcessingDurationMs: 0, PageCount: 0, FullTextLength: 0, LineCount: 0, WordCount: 0,
        ParagraphCount: 0, TableCount: 0, KeyValuePairCount: 0, AverageConfidence: null,
        EstimatedCost: 0, RejectedFieldCount: 0,
        ExtractedSupplierName: null, ExpectedSupplierName: groundTruth?.ExpectedSupplierName, SupplierNameMatched: null,
        ExtractedSupplierTaxCode: null, ExpectedSupplierTaxCode: groundTruth?.ExpectedSupplierTaxCode, TaxCodeMatched: null,
        ExtractedInvoiceNumber: null, ExpectedInvoiceNumber: groundTruth?.ExpectedInvoiceNumber, InvoiceNumberMatched: null,
        ExtractedInvoiceDate: null, ExpectedInvoiceDate: groundTruth?.ExpectedInvoiceDate, InvoiceDateMatched: null,
        ExtractedSubtotalAmount: null, ExpectedSubtotalAmount: groundTruth?.ExpectedSubtotalAmount, SubtotalMatched: null,
        ExtractedVatAmount: null, ExpectedVatAmount: groundTruth?.ExpectedVatAmount, VatMatched: null,
        ExtractedTotalAmount: null, ExpectedTotalAmount: groundTruth?.ExpectedTotalAmount, TotalMatched: null,
        ExtractedCurrency: null, ExpectedCurrency: groundTruth?.ExpectedCurrency, CurrencyMatched: null,
        FieldAccuracyPercent: null, WarningCount: 0,
        RawProviderResponsePath: null, NormalizedOcrResultPath: null, ErrorMessage: errorMessage);

    // ── AverageConfidence: mean confidence across the extracted canonical fields (SupplierName,
    // SupplierTaxCode, InvoiceNumber, ... TotalAmount) — this reflects field-extraction quality,
    // which is what this benchmark compares between providers/strategies. ──────────────────
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

    private static async Task WriteFailureArtifactAsync(string outputDir, string fileName, string errorMessage, CancellationToken ct) =>
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, fileName),
            $$"""{ "success": false, "errorMessage": {{JsonSerializer.Serialize(errorMessage)}} }""",
            ct);

    private static async Task WriteFieldsAndWarningsAsync(
        string outputDir, IReadOnlyList<ExtractedField> fields, IReadOnlyList<ValidationWarning> warnings, CancellationToken ct)
    {
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "extracted-fields.json"),
            JsonSerializer.Serialize(fields, JsonOptions), ct);
        await File.WriteAllTextAsync(
            Path.Combine(outputDir, "validation-warnings.json"),
            JsonSerializer.Serialize(warnings, JsonOptions), ct);
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
