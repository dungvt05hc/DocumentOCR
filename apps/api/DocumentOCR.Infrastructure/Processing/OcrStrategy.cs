using System.Diagnostics;
using System.Text.Json;
using DocumentOCR.Application.Credits;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// <see cref="IDocumentExtractionStrategy"/> wrapping the OCR pipeline: <see cref="IDocumentOcrProvider"/>
/// (still <c>PdfProviderRouter</c>/Fake/Azure/Paddle via DI, unchanged) + <see cref="IFieldExtractionService"/>.
/// The catch-all strategy — <see cref="CanHandleAsync"/> is true for anything that isn't a
/// structured-XML upload (mirrors <see cref="TT78XmlInvoiceParser"/>'s own detection via
/// <see cref="CreditPricing.IsXmlUpload"/>), so an XML file whose <c>XmlInvoiceStrategy</c> attempt
/// fails never falls through to OCR — OCR providers can't meaningfully process XML bytes.
/// </summary>
public sealed class OcrStrategy : IDocumentExtractionStrategy
{
    private readonly IDocumentOcrProvider _ocrProvider;
    private readonly IFieldExtractionService _extraction;
    private readonly IFieldNormalizationService _normalization;
    private readonly IDocumentStorageService _storage;
    private readonly OcrOptions _options;
    private readonly ILogger<OcrStrategy> _logger;

    public string Name => "OcrStrategy";

    public int Priority => 100;

    public OcrStrategy(
        IDocumentOcrProvider ocrProvider,
        IFieldExtractionService extraction,
        IFieldNormalizationService normalization,
        IDocumentStorageService storage,
        IOptions<OcrOptions> options,
        ILogger<OcrStrategy> logger)
    {
        _ocrProvider = ocrProvider;
        _extraction = extraction;
        _normalization = normalization;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public Task<bool> CanHandleAsync(DocumentInput input, CancellationToken ct = default) =>
        Task.FromResult(!CreditPricing.IsXmlUpload(input.ContentType, input.FileName));

    public async Task<StructuredExtractionResult> ExtractAsync(DocumentInput input, CancellationToken ct = default)
    {
        // Defensive: a prior strategy's CanHandleAsync (e.g. a text-layer eligibility probe) may
        // have already read from this stream — always rewind before this strategy reads it too.
        input.Content.Position = 0;

        _logger.LogInformation("Calling OCR provider {ProviderName} for document", _ocrProvider.ProviderName);

        var ocrResult = await AnalyzeWithTimeoutAsync(input, ct);

        _logger.LogInformation(
            "OCR provider {ProviderName} (Model={ModelId}) completed. Success={Success}, Pages={PageCount}, DurationMs={ProcessingTimeMs}, EstimatedCost={EstimatedCost}",
            _ocrProvider.ProviderName, ocrResult.ModelId, ocrResult.Success, ocrResult.PageCount,
            ocrResult.ProcessingTimeMs, ocrResult.EstimatedCost);

        ocrResult = await PersistOcrArtifactsAsync(ocrResult, ct);

        if (!ocrResult.Success)
        {
            return new StructuredExtractionResult
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(ocrResult.ErrorMessage)
                    ? "OCR provider failed without an error message."
                    : ocrResult.ErrorMessage,
                ProviderName = ocrResult.ProviderName,
                ModelId = ocrResult.ModelId,
                PageCount = ocrResult.PageCount,
                ProcessingTimeMs = ocrResult.ProcessingTimeMs,
                EstimatedCost = ocrResult.EstimatedCost,
                RawResponseJson = _options.StoreRawProviderResponse ? ocrResult.RawProviderResponseJson : null,
                RawResponsePath = ocrResult.RawProviderResponsePath,
                NormalizedResultPath = ocrResult.NormalizedOcrResultPath
            };
        }

        var extractedFields = _extraction.Extract(Guid.Empty, ocrResult);

        // Normalized here (not left to DocumentProcessingService's later shared normalize step)
        // because BuildOcrTaxBreakdown below needs Subtotal/VAT's NormalizedValue already
        // populated. FieldNormalizationService.NormalizeFields skips fields that already carry a
        // NormalizedValue, so the shared step running again afterward is a safe no-op.
        _normalization.NormalizeFields(extractedFields);

        // "VatRate" is a candidate-only key (see FieldExtractionService.AddVatRateLineCandidate) —
        // consumed here to synthesize a tax-breakdown row, never persisted as an ExtractedField
        // itself.
        var vatRateField = extractedFields.FirstOrDefault(f => f.FieldName == "VatRate");
        var persistedFields = extractedFields.Where(f => f.FieldName != "VatRate").ToList();

        return new StructuredExtractionResult
        {
            Success = true,
            Fields = persistedFields,
            TaxBreakdown = BuildOcrTaxBreakdown(vatRateField, persistedFields),
            Pages = ocrResult.Pages,
            Tables = ocrResult.Tables.Count > 0 ? ocrResult.Tables : null,
            ProviderName = ocrResult.ProviderName,
            ModelId = ocrResult.ModelId,
            PageCount = ocrResult.PageCount,
            ProcessingTimeMs = ocrResult.ProcessingTimeMs,
            EstimatedCost = ocrResult.EstimatedCost,
            RawResponseJson = _options.StoreRawProviderResponse ? ocrResult.RawProviderResponseJson : null,
            RawResponsePath = ocrResult.RawProviderResponsePath,
            NormalizedResultPath = ocrResult.NormalizedOcrResultPath
        };
    }

    /// <summary>
    /// Hard pipeline-level ceiling (<see cref="OcrOptions.CallTimeoutSeconds"/>, default 120s) on a
    /// single <see cref="IDocumentOcrProvider.AnalyzeAsync"/> call, independent of whatever timeout
    /// (if any) the provider enforces internally -- so a provider that hangs, or a future provider
    /// that forgets its own timeout, can never leave a document stuck in Processing forever. On
    /// expiry this returns a synthetic failed result rather than throwing.
    /// </summary>
    private async Task<NormalizedOcrDocument> AnalyzeWithTimeoutAsync(DocumentInput input, CancellationToken ct)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.CallTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            return await _ocrProvider.AnalyzeAsync(input, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogError(
                "OCR provider {ProviderName} did not respond within {TimeoutSeconds}s.",
                _ocrProvider.ProviderName, _options.CallTimeoutSeconds);

            return new NormalizedOcrDocument
            {
                Success = false,
                ProviderName = _ocrProvider.ProviderName,
                ErrorMessage =
                    $"OCR provider {_ocrProvider.ProviderName} did not respond within " +
                    $"{_options.CallTimeoutSeconds}s."
            };
        }
    }

    private static readonly JsonSerializerOptions ArtifactJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Writes the raw provider response and/or the full normalized OCR result to storage (gated by
    /// <see cref="OcrOptions.StoreRawProviderResponse"/> / <see cref="OcrOptions.StoreNormalizedOcrResult"/>)
    /// and returns an updated <see cref="NormalizedOcrDocument"/> carrying their storage paths. A
    /// provider that returned an unsuccessful result has nothing meaningful to persist here.
    /// </summary>
    private async Task<NormalizedOcrDocument> PersistOcrArtifactsAsync(NormalizedOcrDocument ocrResult, CancellationToken ct)
    {
        if (!ocrResult.Success) return ocrResult;

        string? rawPath = null;
        if (_options.StoreRawProviderResponse && !string.IsNullOrWhiteSpace(ocrResult.RawProviderResponseJson))
        {
            rawPath = await WriteJsonArtifactAsync(
                ocrResult.RawProviderResponseJson, $"{Guid.NewGuid()}-raw-response.json", ct);
        }

        string? normalizedPath = null;
        if (_options.StoreNormalizedOcrResult)
        {
            var normalizedJson = JsonSerializer.Serialize(ocrResult, ArtifactJsonOptions);
            normalizedPath = await WriteJsonArtifactAsync(
                normalizedJson, $"{Guid.NewGuid()}-normalized-ocr-result.json", ct);
        }

        return ocrResult with
        {
            RawProviderResponsePath = rawPath,
            NormalizedOcrResultPath = normalizedPath
        };
    }

    private async Task<string> WriteJsonArtifactAsync(string json, string fileName, CancellationToken ct)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        return await _storage.SaveAsync(stream, fileName, "application/json", ct);
    }

    /// <summary>
    /// Synthesizes a single tax-breakdown row from the OCR pipeline's best-effort "VatRate" line
    /// candidate plus whatever Subtotal/VAT amounts were already extracted — mirrors what TT78 XML
    /// always gets deterministically from THTTLTSuat, but only when a document-level rate was
    /// actually recognized; otherwise the document simply has no breakdown rows (not a fabricated
    /// one at 0/unknown).
    /// </summary>
    private List<InvoiceTaxBreakdown> BuildOcrTaxBreakdown(ExtractedField? vatRateField, IReadOnlyList<ExtractedField> fields)
    {
        if (vatRateField is null) return [];

        var normalizedRate = _normalization.NormalizeVatRate(vatRateField.RawValue);
        if (normalizedRate is null) return [];

        var subtotal = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.SubtotalAmount));
        var vat = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.VatAmount));

        return
        [
            new InvoiceTaxBreakdown
            {
                RawVatRate = vatRateField.RawValue,
                VatRate = normalizedRate,
                TaxableAmount = ParseDecimalOrNull(subtotal?.NormalizedValue),
                TaxAmount = ParseDecimalOrNull(vat?.NormalizedValue),
                Confidence = vatRateField.Confidence,
                SortOrder = 0
            }
        ];
    }

    private static decimal? ParseDecimalOrNull(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
