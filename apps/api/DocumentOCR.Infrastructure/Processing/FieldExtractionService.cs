using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Extracts invoice and receipt fields from structured OCR candidates plus OCR text.
/// The MVP strategy is deterministic: provider fields, keyword rules, nearby line
/// heuristics, and full-text regex fallbacks.
/// </summary>
public partial class FieldExtractionService : IFieldExtractionService
{
    private static readonly Dictionary<string, string> FieldKeyMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "VendorName", nameof(FieldName.SupplierName) },
            { "VendorTaxId", nameof(FieldName.SupplierTaxCode) },
            { "InvoiceId", nameof(FieldName.InvoiceNumber) },
            { "InvoiceDate", nameof(FieldName.InvoiceDate) },
            { "SubTotal", nameof(FieldName.SubtotalAmount) },
            { "TotalTax", nameof(FieldName.VatAmount) },
            { "InvoiceTotal", nameof(FieldName.TotalAmount) },
            { "CurrencyCode", nameof(FieldName.Currency) },
            { "SupplierName", nameof(FieldName.SupplierName) },
            { "SupplierTaxCode", nameof(FieldName.SupplierTaxCode) },
            { "InvoiceNumber", nameof(FieldName.InvoiceNumber) },
            { "SubtotalAmount", nameof(FieldName.SubtotalAmount) },
            { "VatAmount", nameof(FieldName.VatAmount) },
            { "TotalAmount", nameof(FieldName.TotalAmount) },
            { "Notes", nameof(FieldName.Notes) },
            { "DocumentType", nameof(FieldName.DocumentType) }
        };

    private static readonly string[] SupplierKeywords =
    [
        "don vi ban hang", "nguoi ban", "nha cung cap", "supplier", "vendor", "seller"
    ];

    private static readonly string[] TaxCodeKeywords =
    [
        "ma so thue", "mst", "tax code"
    ];

    private static readonly string[] InvoiceNumberKeywords =
    [
        "so hoa don", "so hd", "invoice no", "invoice number", "so"
    ];

    private static readonly string[] InvoiceDateKeywords =
    [
        "ngay hoa don", "invoice date", "ngay"
    ];

    private static readonly string[] SubtotalKeywords =
    [
        "cong tien hang", "subtotal", "thanh tien chua thue", "tien hang"
    ];

    private static readonly string[] VatKeywords =
    [
        "vat", "thue gtgt", "gtgt"
    ];

    private static readonly string[] TotalKeywords =
    [
        "tong thanh toan", "tong cong tien thanh toan", "tong tien", "tong cong", "thanh tien"
    ];

    private static readonly string[] NotesKeywords =
    [
        "ghi chu", "note", "notes", "dien giai", "noi dung"
    ];

    private static readonly string[] SupplierNameMarkers =
    [
        "cong ty", "cty", "tnhh", "co phan", "doanh nghiep", "hop tac xa"
    ];

    public IReadOnlyList<ExtractedField> Extract(Guid documentId, OcrResult ocrResult)
    {
        ArgumentNullException.ThrowIfNull(ocrResult);

        var candidates = new List<FieldCandidate>();
        AddStructuredFieldCandidates(ocrResult, candidates);

        var lines = BuildLineContexts(ocrResult);
        AddLineCandidates(lines, candidates);
        AddFullTextFallbackCandidates(ocrResult, candidates);
        AddDocumentTypeCandidate(ocrResult, lines, candidates);
        AddCurrencyCandidate(ocrResult, lines, candidates);

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.RawValue))
            .GroupBy(c => c.FieldName)
            .Select(g => g
                .OrderByDescending(c => c.Confidence ?? 0)
                .ThenByDescending(c => c.SourcePriority)
                .First())
            .Select(c => new ExtractedField
            {
                DocumentId = documentId,
                FieldName = c.FieldName,
                RawValue = c.RawValue?.Trim(),
                Confidence = c.Confidence,
                PageNumber = c.PageNumber,
                BoundingBoxJson = c.BoundingBox is null ? null : JsonSerializer.Serialize(c.BoundingBox)
            })
            .ToList();
    }

    private static void AddStructuredFieldCandidates(OcrResult ocrResult, List<FieldCandidate> candidates)
    {
        foreach (var ocrField in ocrResult.Fields)
        {
            if (!FieldKeyMap.TryGetValue(ocrField.FieldKey, out var fieldName)) continue;
            if (string.IsNullOrWhiteSpace(ocrField.Value)) continue;

            candidates.Add(new FieldCandidate(
                fieldName,
                ocrField.Value,
                ocrField.Confidence ?? 0.9,
                ocrField.PageNumber,
                ocrField.BoundingBox,
                SourcePriority: 100));
        }
    }

    private static IReadOnlyList<LineContext> BuildLineContexts(OcrResult ocrResult)
    {
        var lines = ocrResult.Pages
            .SelectMany(page => page.Lines.Select(line => new LineContext(
                line.Text,
                NormalizeForSearch(line.Text),
                page.PageNumber,
                line.Confidence ?? page.Confidence ?? ocrResult.Confidence,
                line.BoundingBox)))
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToList();

        if (lines.Count > 0) return lines;

        return SplitLines(ocrResult.FullText)
            .Select(line => new LineContext(line, NormalizeForSearch(line), 1, ocrResult.Confidence, null))
            .ToList();
    }

    private static void AddLineCandidates(IReadOnlyList<LineContext> lines, List<FieldCandidate> candidates)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            AddSupplierNameLineCandidate(lines, candidates, i);
            AddTaxCodeLineCandidate(lines, candidates, i);
            AddInvoiceNumberLineCandidate(lines, candidates, i);
            AddInvoiceDateLineCandidate(lines, candidates, i);
            AddAmountLineCandidates(lines, candidates, i);
            AddNotesLineCandidate(lines, candidates, i);

            if (i < 10
                && ContainsAny(line.SearchText, SupplierNameMarkers)
                && !ContainsAny(line.SearchText, SupplierKeywords))
            {
                candidates.Add(new FieldCandidate(
                    nameof(FieldName.SupplierName),
                    CleanValue(line.Text),
                    Score(line, 0.72),
                    line.PageNumber,
                    line.BoundingBox,
                    SourcePriority: 30));
            }
        }
    }

    private static void AddSupplierNameLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        if (!ContainsAny(line.SearchText, SupplierKeywords)) return;

        var value = ValueAfterLabel(line.Text);
        if (string.IsNullOrWhiteSpace(value) && TryGetNearbyLine(lines, index, out var nearby))
        {
            value = nearby.Text;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        candidates.Add(new FieldCandidate(
            nameof(FieldName.SupplierName),
            CleanValue(value),
            Score(line, 0.88),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 60));
    }

    private static void AddTaxCodeLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        if (!ContainsAny(line.SearchText, TaxCodeKeywords)) return;

        var value = TaxCodeValuePattern().Match(line.Text).Value;
        if (string.IsNullOrWhiteSpace(value) && TryGetNearbyLine(lines, index, out var nearby))
        {
            value = TaxCodeValuePattern().Match(nearby.Text).Value;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        candidates.Add(new FieldCandidate(
            nameof(FieldName.SupplierTaxCode),
            value,
            Score(line, 0.9),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 70));
    }

    private static void AddInvoiceNumberLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        if (!ContainsAny(line.SearchText, InvoiceNumberKeywords)) return;
        if (line.SearchText.Contains("ma so thue", StringComparison.Ordinal)) return;

        var value = InvoiceNumberValuePattern().Match(ValueAfterLabel(line.Text) ?? line.Text).Value;
        if (string.IsNullOrWhiteSpace(value) && TryGetNearbyLine(lines, index, out var nearby))
        {
            value = InvoiceNumberValuePattern().Match(nearby.Text).Value;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        candidates.Add(new FieldCandidate(
            nameof(FieldName.InvoiceNumber),
            value,
            Score(line, line.SearchText == "so" ? 0.72 : 0.86),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 55));
    }

    private static void AddInvoiceDateLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        if (!ContainsAny(line.SearchText, InvoiceDateKeywords)) return;

        var value = DateValuePattern().Match(line.Text).Value;
        if (string.IsNullOrWhiteSpace(value) && TryGetNearbyLine(lines, index, out var nearby))
        {
            value = DateValuePattern().Match(nearby.Text).Value;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        candidates.Add(new FieldCandidate(
            nameof(FieldName.InvoiceDate),
            value,
            Score(line, 0.86),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 55));
    }

    private static void AddAmountLineCandidates(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        var fieldName = GetAmountFieldName(line.SearchText);
        if (fieldName is null) return;

        var value = LastMoneyValue(line.Text);
        if (string.IsNullOrWhiteSpace(value) && TryGetNearbyLine(lines, index, out var nearby))
        {
            value = LastMoneyValue(nearby.Text);
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        var baseScore = fieldName == nameof(FieldName.TotalAmount) ? 0.88 : 0.84;
        candidates.Add(new FieldCandidate(
            fieldName,
            value,
            Score(line, baseScore),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 65));
    }

    private static void AddNotesLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        if (!ContainsAny(line.SearchText, NotesKeywords)) return;

        var value = ValueAfterLabel(line.Text);
        if (string.IsNullOrWhiteSpace(value) && TryGetNearbyLine(lines, index, out var nearby))
        {
            value = nearby.Text;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        candidates.Add(new FieldCandidate(
            nameof(FieldName.Notes),
            CleanValue(value),
            Score(line, 0.78),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 35));
    }

    private static void AddFullTextFallbackCandidates(OcrResult ocrResult, List<FieldCandidate> candidates)
    {
        var fullText = GetFullText(ocrResult);
        if (string.IsNullOrWhiteSpace(fullText)) return;

        AddRegexFallback(candidates, nameof(FieldName.SupplierTaxCode), fullText, TaxCodeFallbackPattern(), 0.64);
        AddRegexFallback(candidates, nameof(FieldName.InvoiceNumber), fullText, InvoiceNumberFallbackPattern(), 0.62);
        AddRegexFallback(candidates, nameof(FieldName.InvoiceDate), fullText, DateValuePattern(), 0.6);
        AddRegexFallback(candidates, nameof(FieldName.TotalAmount), fullText, TotalAmountFallbackPattern(), 0.62, useLastGroup: true);
        AddRegexFallback(candidates, nameof(FieldName.VatAmount), fullText, VatAmountFallbackPattern(), 0.6, useLastGroup: true);
        AddRegexFallback(candidates, nameof(FieldName.SubtotalAmount), fullText, SubtotalFallbackPattern(), 0.6, useLastGroup: true);
    }

    private static void AddRegexFallback(
        List<FieldCandidate> candidates,
        string fieldName,
        string text,
        Regex pattern,
        double confidence,
        bool useLastGroup = false)
    {
        var match = pattern.Match(text);
        if (!match.Success) return;

        var value = useLastGroup
            ? match.Groups[^1].Value
            : match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;

        candidates.Add(new FieldCandidate(fieldName, value, confidence, null, null, SourcePriority: 10));
    }

    private static void AddDocumentTypeCandidate(
        OcrResult ocrResult,
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates)
    {
        var searchText = NormalizeForSearch(GetFullText(ocrResult));
        if (string.IsNullOrWhiteSpace(searchText))
        {
            searchText = string.Join('\n', lines.Select(l => l.SearchText));
        }

        var originalText = GetFullText(ocrResult);

        string? documentType = null;
        if (ReceiptDocumentPattern().IsMatch(originalText)
            || searchText.Contains("bien lai", StringComparison.Ordinal)
            || searchText.Contains("phieu thu", StringComparison.Ordinal)
            || searchText.Contains("receipt", StringComparison.Ordinal))
        {
            documentType = nameof(DocumentType.Receipt);
        }
        else if (InvoiceDocumentPattern().IsMatch(originalText)
                 || searchText.Contains("hoa don", StringComparison.Ordinal)
                 || searchText.Contains("invoice", StringComparison.Ordinal))
        {
            documentType = nameof(DocumentType.Invoice);
        }

        if (documentType is null) return;

        candidates.Add(new FieldCandidate(
            nameof(FieldName.DocumentType),
            documentType,
            0.84,
            lines.FirstOrDefault().PageNumber,
            lines.FirstOrDefault().BoundingBox,
            SourcePriority: 45));
    }

    private static void AddCurrencyCandidate(
        OcrResult ocrResult,
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates)
    {
        var fullText = GetFullText(ocrResult);
        var match = CurrencyMarkerPattern().Match(fullText);
        if (!match.Success) return;

        var line = lines.FirstOrDefault(l => CurrencyMarkerPattern().IsMatch(l.Text));
        candidates.Add(new FieldCandidate(
            nameof(FieldName.Currency),
            match.Value,
            0.78,
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 35));
    }

    private static string? GetAmountFieldName(string searchText)
    {
        if (ContainsAny(searchText, VatKeywords)) return nameof(FieldName.VatAmount);
        if (ContainsAny(searchText, SubtotalKeywords)) return nameof(FieldName.SubtotalAmount);
        if (ContainsAny(searchText, TotalKeywords)) return nameof(FieldName.TotalAmount);

        return null;
    }

    private static string? LastMoneyValue(string value)
    {
        return MoneyValuePattern().Matches(value)
            .Cast<Match>()
            .LastOrDefault(m => m.Value.Any(char.IsDigit))
            ?.Value;
    }

    private static bool TryGetNearbyLine(
        IReadOnlyList<LineContext> lines,
        int index,
        out LineContext nearby)
    {
        for (var i = index + 1; i < Math.Min(lines.Count, index + 3); i++)
        {
            if (!string.IsNullOrWhiteSpace(lines[i].Text))
            {
                nearby = lines[i];
                return true;
            }
        }

        nearby = default;
        return false;
    }

    private static string? ValueAfterLabel(string text)
    {
        var separatorIndex = text.IndexOf(':');
        if (separatorIndex < 0) separatorIndex = text.IndexOf('-');

        return separatorIndex >= 0 && separatorIndex + 1 < text.Length
            ? text[(separatorIndex + 1)..].Trim()
            : null;
    }

    private static double Score(LineContext line, double baseScore)
    {
        return line.Confidence is null
            ? baseScore
            : Math.Round((baseScore * 0.7) + (Math.Clamp(line.Confidence.Value, 0, 1) * 0.3), 4);
    }

    private static bool ContainsAny(string searchText, IEnumerable<string> keywords)
    {
        return keywords.Any(keyword => searchText.Contains(keyword, StringComparison.Ordinal));
    }

    private static string CleanValue(string value)
    {
        return WhitespacePattern().Replace(value.Trim(' ', ':', '-', ';'), " ");
    }

    private static string GetFullText(OcrResult ocrResult)
    {
        if (!string.IsNullOrWhiteSpace(ocrResult.FullText)) return ocrResult.FullText;
        return string.Join('\n', ocrResult.Pages.Select(p => p.FullText));
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        return text.Split(["\r\n", "\n", "\r"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string NormalizeForSearch(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c switch
                {
                    'Đ' => 'd',
                    'đ' => 'd',
                    _ => char.ToLowerInvariant(c)
                });
            }
        }

        return WhitespacePattern().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    private sealed record FieldCandidate(
        string FieldName,
        string? RawValue,
        double? Confidence,
        int? PageNumber,
        BoundingBox? BoundingBox,
        int SourcePriority);

    private readonly record struct LineContext(
        string Text,
        string SearchText,
        int? PageNumber,
        double? Confidence,
        BoundingBox? BoundingBox);

    [GeneratedRegex(@"\d{10}(?:[-\s]?\d{3})?")]
    private static partial Regex TaxCodeValuePattern();

    [GeneratedRegex(@"[A-Z0-9][A-Z0-9\-/.]{2,}", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceNumberValuePattern();

    [GeneratedRegex(@"\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{1,2}\.\d{1,2}\.\d{2,4}|\d{4}-\d{1,2}-\d{1,2}")]
    private static partial Regex DateValuePattern();

    [GeneratedRegex(@"(?:VND|VNĐ|₫|đ)?\s*\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyValuePattern();

    [GeneratedRegex(@"(?:VND|VNĐ|₫|đồng|dong)", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyMarkerPattern();

    [GeneratedRegex(@"(?:HÓA\s*ĐƠN|HOA\s*DON|Invoice)", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceDocumentPattern();

    [GeneratedRegex(@"(?:BIÊN\s*LAI|BIEN\s*LAI|PHIẾU\s*THU|PHIEU\s*THU|Receipt)", RegexOptions.IgnoreCase)]
    private static partial Regex ReceiptDocumentPattern();

    [GeneratedRegex(@"(?:Mã\s*số\s*thuế|MST|Tax\s*code)\s*[:\-]?\s*([0-9][0-9\s\-.]{8,18})", RegexOptions.IgnoreCase)]
    private static partial Regex TaxCodeFallbackPattern();

    [GeneratedRegex(@"(?:Số\s*hóa\s*đơn|Số\s*HĐ|Invoice\s*(?:No|Number)|Số)\s*[:\-]?\s*([A-Z0-9][A-Z0-9\-/.]{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceNumberFallbackPattern();

    [GeneratedRegex(@"(?:Tổng\s*thanh\s*toán|Tổng\s*cộng(?:\s*tiền\s*thanh\s*toán)?|Tổng\s*tiền|Thành\s*tiền)\s*[:\-]?\s*((?:VND|VNĐ|₫|đ)?\s*\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?)", RegexOptions.IgnoreCase)]
    private static partial Regex TotalAmountFallbackPattern();

    [GeneratedRegex(@"(?:VAT|Thuế\s*GTGT)\D*((?:VND|VNĐ|₫|đ)?\s*\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?)", RegexOptions.IgnoreCase)]
    private static partial Regex VatAmountFallbackPattern();

    [GeneratedRegex(@"(?:Cộng\s*tiền\s*hàng|Subtotal|Thành\s*tiền\s*chưa\s*thuế)\s*[:\-]?\s*((?:VND|VNĐ|₫|đ)?\s*\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?)", RegexOptions.IgnoreCase)]
    private static partial Regex SubtotalFallbackPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
