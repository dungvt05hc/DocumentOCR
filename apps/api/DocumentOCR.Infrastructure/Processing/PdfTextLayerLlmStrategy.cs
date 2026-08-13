using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentOCR.Application.Credits;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Llm;
using DocumentOCR.Infrastructure.Ocr;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// <see cref="IDocumentExtractionStrategy"/> for software-generated PDFs (MISA/Viettel/VNPT/BKAV
/// e-invoices): reads the embedded text layer and hands it to an LLM for field recognition,
/// instead of the heuristic <c>FieldExtractionService</c> <see cref="OcrStrategy"/> uses. Text
/// extraction itself is not reimplemented here — reuses <see cref="PdfTextLayerProvider"/>
/// (already tuned for reading-order text + the "&lt;100 chars = scan" threshold via
/// <c>Ocr:PdfTextLayer:MinExtractedCharacters</c>), the same provider <c>PdfProviderRouter</c>
/// already uses for the free heuristic path.
/// <para>
/// The LLM is never trusted directly: every non-null <see cref="LlmFieldValue"/> must have a
/// <c>SourceText</c> that actually occurs (after whitespace normalization) in the extracted PDF
/// text, or the field is dropped entirely — see <see cref="IsSourceTextVerified"/>. Confidence is
/// always computed here from verification + format/consistency checks, never taken from the LLM.
/// </para>
/// <para>
/// Depends on <see cref="IDocumentOcrProvider"/> rather than the concrete
/// <see cref="PdfTextLayerProvider"/> purely for testability — same pattern as
/// <c>PdfProviderRouter</c>; production DI still resolves the real <see cref="PdfTextLayerProvider"/>
/// singleton for this parameter (see <c>DependencyInjection.cs</c>).
/// </para>
/// <para>
/// Registered Scoped (one instance per document-processing job) so the single text-layer read
/// <see cref="CanHandleAsync"/> performs can be reused by <see cref="ExtractAsync"/> without
/// re-parsing the PDF with PdfPig a second time.
/// </para>
/// <para>
/// LLM responses are cached (<see cref="ILlmExtractionCache"/>) by (text hash, model) so
/// re-processing the same PDF text never re-calls a paid LLM — a cache hit still goes through the
/// full verification/confidence pipeline below, only the network call is skipped.
/// </para>
/// </summary>
public sealed class PdfTextLayerLlmStrategy : IDocumentExtractionStrategy
{
    private const string PdfContentType = "application/pdf";

    /// <summary>Single source of truth shared with <see cref="CreditPricing.ResolveActualCost"/>.</summary>
    private const string ProviderName = CreditPricing.PdfTextLayerLlmProviderName;

    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions CacheJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IDocumentOcrProvider _pdfTextLayerProvider;
    private readonly ILlmExtractionClient _llmClient;
    private readonly IFieldNormalizationService _normalization;
    private readonly ILlmExtractionCache _cache;
    private readonly LlmOptions _options;
    private readonly ILogger<PdfTextLayerLlmStrategy> _logger;

    private NormalizedOcrDocument? _textLayerResult;

    public string Name => "PdfTextLayerLlmStrategy";

    public int Priority => 50;

    public PdfTextLayerLlmStrategy(
        IDocumentOcrProvider pdfTextLayerProvider,
        ILlmExtractionClient llmClient,
        IFieldNormalizationService normalization,
        ILlmExtractionCache cache,
        IOptions<LlmOptions> options,
        ILogger<PdfTextLayerLlmStrategy> logger)
    {
        _pdfTextLayerProvider = pdfTextLayerProvider;
        _llmClient = llmClient;
        _normalization = normalization;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> CanHandleAsync(DocumentInput input, CancellationToken ct = default)
    {
        if (!_options.Enabled) return false;

        if (!string.Equals(input.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase))
            return false;

        var result = await ReadTextLayerAsync(input, ct);

        if (!result.Success)
        {
            _logger.LogInformation(
                "PdfTextLayerLlmStrategy cannot handle '{FileName}': {Reason}",
                input.FileName, result.ErrorMessage);
        }

        return result.Success;
    }

    public async Task<StructuredExtractionResult> ExtractAsync(DocumentInput input, CancellationToken ct = default)
    {
        var textLayer = await ReadTextLayerAsync(input, ct);
        if (!textLayer.Success)
        {
            return Failure("PDF text layer could not be read.", textLayer.PageCount);
        }

        var normalizedFullText = NormalizeWhitespace(textLayer.FullText);
        var textHash = ComputeTextHash(normalizedFullText);

        LlmExtractionResult? llmResult = await TryGetCachedResultAsync(textHash, ct);

        if (llmResult is null)
        {
            try
            {
                llmResult = await _llmClient.ExtractAsync(textLayer.FullText, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM extraction failed for '{FileName}'.", input.FileName);
                return Failure($"LLM extraction failed: {ex.Message}", textLayer.PageCount);
            }

            await _cache.SetAsync(textHash, _options.Model, JsonSerializer.Serialize(llmResult, CacheJsonOptions), ct);
        }
        else
        {
            _logger.LogInformation("LLM extraction cache hit for text hash {TextHash}.", textHash);
        }

        var fields = new List<ExtractedField>();
        var rejectedFieldCount = 0;

        AddTextField(fields, nameof(FieldName.InvoiceForm), llmResult.MauSo, normalizedFullText, "mauSo", ref rejectedFieldCount);
        AddTextField(fields, nameof(FieldName.InvoiceSymbol), llmResult.KyHieu, normalizedFullText, "kyHieu", ref rejectedFieldCount);
        AddTextField(fields, nameof(FieldName.InvoiceNumber), llmResult.SoHoaDon, normalizedFullText, "soHoaDon", ref rejectedFieldCount);
        AddDateField(fields, llmResult.NgayLap, normalizedFullText, ref rejectedFieldCount);
        AddTextField(fields, nameof(FieldName.TaxAuthorityCode), llmResult.MaCqt, normalizedFullText, "maCqt", ref rejectedFieldCount);
        AddTextField(fields, "LookupCode", llmResult.MaTraCuu, normalizedFullText, "maTraCuu", ref rejectedFieldCount);
        AddTextField(fields, nameof(FieldName.SupplierName), llmResult.NguoiBanTen, normalizedFullText, "nguoiBanTen", ref rejectedFieldCount);
        AddTaxCodeField(fields, nameof(FieldName.SupplierTaxCode), llmResult.NguoiBanMst, normalizedFullText, "nguoiBanMst", ref rejectedFieldCount);
        AddTextField(fields, nameof(FieldName.SupplierAddress), llmResult.NguoiBanDiaChi, normalizedFullText, "nguoiBanDiaChi", ref rejectedFieldCount);
        AddTextField(fields, nameof(FieldName.BuyerName), llmResult.NguoiMuaTen, normalizedFullText, "nguoiMuaTen", ref rejectedFieldCount);
        AddTaxCodeField(fields, nameof(FieldName.BuyerTaxCode), llmResult.NguoiMuaMst, normalizedFullText, "nguoiMuaMst", ref rejectedFieldCount);
        AddTextField(fields, nameof(FieldName.BuyerAddress), llmResult.NguoiMuaDiaChi, normalizedFullText, "nguoiMuaDiaChi", ref rejectedFieldCount);
        AddMoneyField(fields, nameof(FieldName.SubtotalAmount), llmResult.TienHang, normalizedFullText, "tienHang", ref rejectedFieldCount);
        AddMoneyField(fields, nameof(FieldName.VatAmount), llmResult.TongTienThue, normalizedFullText, "tongTienThue", ref rejectedFieldCount);
        AddMoneyField(fields, nameof(FieldName.TotalAmount), llmResult.TongThanhToan, normalizedFullText, "tongThanhToan", ref rejectedFieldCount);
        AddTextField(fields, "AmountInWords", llmResult.TienBangChu, normalizedFullText, "tienBangChu", ref rejectedFieldCount);
        AddTextField(fields, "PaymentMethod", llmResult.HinhThucThanhToan, normalizedFullText, "hinhThucThanhToan", ref rejectedFieldCount);
        AddTextField(fields, nameof(FieldName.Currency), llmResult.DongTien, normalizedFullText, "dongTien", ref rejectedFieldCount);

        // Synthesized, not read from the LLM — this pipeline exists specifically for Vietnamese VAT
        // e-invoices (see the class remarks), mirroring TT78XmlInvoiceParser's own DocumentType field.
        fields.Add(new ExtractedField
        {
            FieldName = nameof(FieldName.DocumentType),
            RawValue = nameof(DocumentType.VatInvoice),
            NormalizedValue = nameof(DocumentType.VatInvoice),
            Confidence = 1.0,
            SourceType = ProviderName,
            ExtractionMethod = ProviderName
        });

        ReconcileAmountConsistency(fields);

        var taxBreakdown = BuildTaxBreakdown(llmResult.ChiTietThueSuat, normalizedFullText, ref rejectedFieldCount);

        return new StructuredExtractionResult
        {
            Success = true,
            Fields = fields,
            TaxBreakdown = taxBreakdown,
            RawSourceText = textLayer.FullText,
            ProviderName = ProviderName,
            ModelId = _options.Model,
            PageCount = textLayer.PageCount,
            EstimatedCost = llmResult.Usage.EstimatedCostUsd,
            RejectedFieldCount = rejectedFieldCount
        };
    }

    // ── Field verification + confidence ─────────────────────────────────────────

    /// <summary>Plain-text field with no applicable format check — 0.9 confidence whenever sourceText verifies, otherwise dropped.</summary>
    private void AddTextField(
        List<ExtractedField> fields, string fieldName, LlmFieldValue? llmValue, string normalizedFullText, string providerFieldName, ref int rejectedFieldCount)
    {
        if (!TryVerify(fieldName, llmValue, normalizedFullText, ref rejectedFieldCount)) return;

        fields.Add(new ExtractedField
        {
            FieldName = fieldName,
            RawValue = llmValue!.Value,
            Confidence = 0.9,
            SourceType = ProviderName,
            ExtractionMethod = ProviderName,
            ProviderFieldName = providerFieldName,
            SourceText = llmValue.SourceText
        });
    }

    private void AddDateField(List<ExtractedField> fields, LlmFieldValue? llmValue, string normalizedFullText, ref int rejectedFieldCount)
    {
        const string fieldName = nameof(FieldName.InvoiceDate);
        if (!TryVerify(fieldName, llmValue, normalizedFullText, ref rejectedFieldCount)) return;

        var normalizedDate = _normalization.NormalizeDate(llmValue!.Value);

        fields.Add(new ExtractedField
        {
            FieldName = fieldName,
            RawValue = llmValue.Value,
            NormalizedValue = normalizedDate?.ToString("yyyy-MM-dd"),
            Confidence = normalizedDate is not null ? 0.9 : 0.5,
            SourceType = ProviderName,
            ExtractionMethod = ProviderName,
            ProviderFieldName = "ngayLap",
            SourceText = llmValue.SourceText
        });
    }

    /// <summary>Format check mirrors <c>FieldValidationService</c>'s own tax-code rule: 10 or 13 digits after normalization — no checksum algorithm (see docs/decisions.md).</summary>
    private void AddTaxCodeField(
        List<ExtractedField> fields, string fieldName, LlmFieldValue? llmValue, string normalizedFullText, string providerFieldName, ref int rejectedFieldCount)
    {
        if (!TryVerify(fieldName, llmValue, normalizedFullText, ref rejectedFieldCount)) return;

        var normalizedTaxCode = _normalization.NormalizeTaxCode(llmValue!.Value);
        var isValidFormat = normalizedTaxCode is { Length: 10 or 13 };

        fields.Add(new ExtractedField
        {
            FieldName = fieldName,
            RawValue = llmValue.Value,
            NormalizedValue = normalizedTaxCode,
            Confidence = isValidFormat ? 0.9 : 0.5,
            SourceType = ProviderName,
            ExtractionMethod = ProviderName,
            ProviderFieldName = providerFieldName,
            SourceText = llmValue.SourceText
        });
    }

    private void AddMoneyField(
        List<ExtractedField> fields, string fieldName, LlmFieldValue? llmValue, string normalizedFullText, string providerFieldName, ref int rejectedFieldCount)
    {
        if (!TryVerify(fieldName, llmValue, normalizedFullText, ref rejectedFieldCount)) return;

        var normalizedAmount = _normalization.NormalizeCurrency(llmValue!.Value);

        fields.Add(new ExtractedField
        {
            FieldName = fieldName,
            RawValue = llmValue.Value,
            NormalizedValue = normalizedAmount?.ToString(CultureInfo.InvariantCulture),
            Confidence = normalizedAmount is not null ? 0.9 : 0.5,
            SourceType = ProviderName,
            ExtractionMethod = ProviderName,
            ProviderFieldName = providerFieldName,
            SourceText = llmValue.SourceText
        });
    }

    /// <summary>
    /// Downgrades SubtotalAmount/VatAmount/TotalAmount together from 0.9 to 0.5 when all three
    /// parsed successfully but don't reconcile (tolerance 1 VND) — only applies when all three are
    /// present; a field already missing/unparseable is left alone (already at 0.5 or absent).
    /// <see cref="Application.Processing.FieldValidationService"/>'s TOTAL_MISMATCH warning
    /// independently flags this too, from the same normalized values.
    /// </summary>
    private static void ReconcileAmountConsistency(List<ExtractedField> fields)
    {
        var subtotal = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.SubtotalAmount));
        var vat = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.VatAmount));
        var total = fields.FirstOrDefault(f => f.FieldName == nameof(FieldName.TotalAmount));

        if (!TryParseMoney(subtotal?.NormalizedValue, out var subtotalValue)) return;
        if (!TryParseMoney(vat?.NormalizedValue, out var vatValue)) return;
        if (!TryParseMoney(total?.NormalizedValue, out var totalValue)) return;

        if (Math.Abs(subtotalValue + vatValue - totalValue) <= 1m) return;

        subtotal!.Confidence = 0.5;
        vat!.Confidence = 0.5;
        total!.Confidence = 0.5;
    }

    private List<InvoiceTaxBreakdown> BuildTaxBreakdown(IReadOnlyList<LlmTaxBreakdownLine> lines, string normalizedFullText, ref int rejectedFieldCount)
    {
        var result = new List<InvoiceTaxBreakdown>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            // An incomplete row (any of the three pieces missing) is dropped entirely rather than
            // persisted with a fabricated gap — same "missing = absent, never guessed" rule as
            // individual fields. Not counted as "rejected" -- that label is reserved for values
            // the LLM did provide but whose sourceText failed verification (see below).
            if (line.ThueSuat?.Value is null || line.TienChuaThue?.Value is null || line.TienThue?.Value is null)
                continue;

            if (!IsSourceTextVerified(line.ThueSuat.SourceText, normalizedFullText) ||
                !IsSourceTextVerified(line.TienChuaThue.SourceText, normalizedFullText) ||
                !IsSourceTextVerified(line.TienThue.SourceText, normalizedFullText))
            {
                rejectedFieldCount++;
                _logger.LogWarning("LLM tax breakdown line {Index} rejected: sourceText not found in extracted PDF text.", i);
                continue;
            }

            var normalizedRate = _normalization.NormalizeVatRate(line.ThueSuat.Value);
            var taxable = _normalization.NormalizeCurrency(line.TienChuaThue.Value);
            var tax = _normalization.NormalizeCurrency(line.TienThue.Value);
            var isConsistent = normalizedRate is not null && taxable is not null && tax is not null;

            result.Add(new InvoiceTaxBreakdown
            {
                RawVatRate = line.ThueSuat.Value,
                VatRate = normalizedRate,
                TaxableAmount = taxable,
                TaxAmount = tax,
                Confidence = isConsistent ? 0.9 : 0.5,
                SortOrder = result.Count
            });
        }

        return result;
    }

    /// <summary>
    /// Anti-hallucination gate shared by every Add*Field helper: a null <see cref="LlmFieldValue.Value"/>
    /// means "not found" (nothing to add, never a guess); a non-null value whose SourceText doesn't
    /// verify against the extracted text is dropped and logged by field name only — never logs the
    /// (potentially fabricated) value itself.
    /// </summary>
    private bool TryVerify(string fieldName, LlmFieldValue? llmValue, string normalizedFullText, ref int rejectedFieldCount)
    {
        if (llmValue?.Value is null) return false;

        if (IsSourceTextVerified(llmValue.SourceText, normalizedFullText)) return true;

        rejectedFieldCount++;
        _logger.LogWarning(
            "LLM field {FieldName} rejected: sourceText not found in extracted PDF text.", fieldName);
        return false;
    }

    private static bool IsSourceTextVerified(string? sourceText, string normalizedFullText)
    {
        if (string.IsNullOrWhiteSpace(sourceText)) return false;

        var normalizedSourceText = NormalizeWhitespace(sourceText);
        return normalizedSourceText.Length > 0 &&
            normalizedFullText.Contains(normalizedSourceText, StringComparison.Ordinal);
    }

    private static string NormalizeWhitespace(string value) => WhitespaceRegex.Replace(value, " ").Trim();

    /// <summary>
    /// A corrupt/unreadable cache entry (e.g. schema drift from an older cached JSON shape) is
    /// treated the same as a cache miss — falls through to a fresh LLM call rather than failing
    /// the document.
    /// </summary>
    private async Task<LlmExtractionResult?> TryGetCachedResultAsync(string textHash, CancellationToken ct)
    {
        var cachedJson = await _cache.TryGetResponseJsonAsync(textHash, _options.Model, ct);
        if (cachedJson is null) return null;

        try
        {
            return JsonSerializer.Deserialize<LlmExtractionResult>(cachedJson, CacheJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "LLM extraction cache entry for hash {TextHash} could not be deserialized; re-calling LLM.", textHash);
            return null;
        }
    }

    private static string ComputeTextHash(string normalizedText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText)));

    private static bool TryParseMoney(string? value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static StructuredExtractionResult Failure(string errorMessage, int pageCount) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        ProviderName = ProviderName,
        PageCount = pageCount
    };

    /// <summary>
    /// Reads the PDF's text layer at most once per instance — this strategy is Scoped (one
    /// instance per document-processing job), and both <see cref="CanHandleAsync"/>'s eligibility
    /// check and <see cref="ExtractAsync"/>'s actual extraction need the same read.
    /// </summary>
    private async Task<NormalizedOcrDocument> ReadTextLayerAsync(DocumentInput input, CancellationToken ct)
    {
        if (_textLayerResult is not null) return _textLayerResult;

        input.Content.Position = 0;
        _textLayerResult = await _pdfTextLayerProvider.AnalyzeAsync(input, ct);
        return _textLayerResult;
    }
}
