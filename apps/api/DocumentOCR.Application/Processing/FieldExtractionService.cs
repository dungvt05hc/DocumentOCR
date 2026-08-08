using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Processing;

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
        "so hoa don", "so hd", "so phieu", "ma hd", "invoice no", "invoice number", "so"
    ];

    private static readonly string[] InvoiceDateKeywords =
    [
        "ngay hoa don", "ngay tao", "invoice date", "ngay", "date"
    ];

    private static readonly string[] DueDateKeywords = ["due date"];

    private static readonly string[] PoNumberKeywords = ["po number", "purchase order"];

    private static readonly string[] SubtotalKeywords =
    [
        "cong tien hang", "subtotal", "sub total", "sub_total", "thanh tien chua thue", "tien hang", "thanh tien", "tam tinh"
    ];

    private static readonly string[] VatKeywords =
    [
        "vat", "thue gtgt", "tien thue gtgt", "gtgt"
    ];

    /// <summary>
    /// TotalAmount keyword strength, weakest to strongest. A bare "Tổng" is a valid but weak
    /// signal; more specific phrasing like "Tổng thanh toán" should win when both are present
    /// on the same document.
    /// </summary>
    private static readonly (string Keyword, int Weight)[] TotalKeywordWeights =
    [
        ("tong thanh toan", 3),
        ("tong cong tien thanh toan", 3),
        ("tong cong", 2),
        ("tong tien", 2),
        ("thanh toan", 2),
        ("tong", 1),
        ("total", 2)
    ];

    private static readonly string[] NotesKeywords =
    [
        "ghi chu", "note", "notes", "dien giai", "noi dung"
    ];

    private static readonly string[] SupplierNameMarkers =
    [
        "cong ty", "cty", "tnhh", "co phan", "doanh nghiep", "hop tac xa"
    ];

    /// <summary>
    /// Lines that look like an address, phone number, date, or document-type title are not
    /// merchant-name candidates for the top-line supplier heuristic even though they may sit
    /// above the first genuinely useful field on a POS receipt.
    /// </summary>
    private static readonly string[] AddressMarkers =
    [
        "tp.", "thanh pho", "phuong", "quan", "huyen", "tinh", "duong", "so nha"
    ];

    private const int TopLineSupplierScanWindow = 6;

    public IReadOnlyList<ExtractedField> Extract(Guid documentId, NormalizedOcrDocument ocrResult)
    {
        ArgumentNullException.ThrowIfNull(ocrResult);

        // Collect candidates from every source unconditionally, then pick the best one per
        // field by confidence (SourcePriority only breaks ties) — this lets a high-confidence
        // KeyValuePair or Table hit win over a weaker Line/Regex candidate for the same field
        // without needing a strict "stop at first match" cascade.
        var candidates = new List<FieldCandidate>();
        AddKeyValuePairCandidates(ocrResult, candidates);       // 1. KeyValuePairs (prebuilt-layout)
        AddStructuredFieldCandidates(ocrResult, candidates);    // 2. Provider semantic fields (prebuilt-invoice/receipt)

        var lines = BuildLineContexts(ocrResult);
        AddFullTextFallbackCandidates(ocrResult, candidates);   // 3. Regex over FullText
        AddParagraphFallbackCandidates(ocrResult, candidates);  // 4. Regex over Paragraphs
        AddLineCandidates(lines, candidates);                   // 5. Line proximity around keywords
        AddTableFooterCandidates(ocrResult, candidates);        // 6. Table/footer heuristics
        AddSupplierNameTopLineCandidate(lines, candidates);     // 7. Top-line merchant heuristic
        AddDocumentTypeCandidate(ocrResult, lines, candidates);
        AddCurrencyCandidate(ocrResult, lines, candidates);
        AddDefaultCurrencyCandidate(candidates);

        return candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.RawValue))
            // A website/domain is never a real invoice number — without this, a normalized
            // search-text false-positive (e.g. "so" matching inside an unrelated word within a
            // URL) can otherwise let InvoiceNumberValuePattern swallow the whole line.
            .Where(c => c.FieldName != nameof(FieldName.InvoiceNumber) || !UrlLikePattern().IsMatch(c.RawValue!))
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
                BoundingBoxJson = c.BoundingBox is null ? null : JsonSerializer.Serialize(c.BoundingBox),
                ExtractionMethod = c.SourceMethod,
                SourceText = Truncate(c.SourceText, 300),
                SourceType = c.SourceType,
                ProviderFieldName = c.ProviderFieldName
            })
            .ToList();
    }

    private static void AddStructuredFieldCandidates(NormalizedOcrDocument ocrResult, List<FieldCandidate> candidates)
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
                SourcePriority: 100,
                SourceMethod: "StructuredField",
                SourceText: ocrField.Value,
                SourceType: "StructuredField",
                ProviderFieldName: ocrField.RawProviderKey));
        }
    }

    /// <summary>
    /// Maps the OCR provider's free-form layout key-value pairs (Azure's "keyValuePairs"
    /// add-on feature) onto canonical fields using the same Vietnamese keyword vocabulary as
    /// the line-based heuristics below. This is the primary extraction path for
    /// prebuilt-layout, which — unlike prebuilt-invoice/receipt — never populates
    /// <see cref="NormalizedOcrDocument.Fields"/>.
    /// </summary>
    private static void AddKeyValuePairCandidates(NormalizedOcrDocument ocrResult, List<FieldCandidate> candidates)
    {
        foreach (var kvp in ocrResult.KeyValuePairs)
        {
            if (string.IsNullOrWhiteSpace(kvp.ValueText)) continue;

            var keySearch = NormalizeForSearch(kvp.KeyText);
            var confidence = kvp.Confidence ?? 0.9;
            var boundingBox = kvp.ValueBoundingBox ?? kvp.KeyBoundingBox;
            var sourceText = $"{kvp.KeyText}: {kvp.ValueText}";

            void Add(string fieldName, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                candidates.Add(new FieldCandidate(
                    fieldName, value, confidence, kvp.PageNumber, boundingBox,
                    SourcePriority: 105, SourceMethod: "KeyValuePair", SourceText: sourceText,
                    SourceType: "KeyValuePair", ProviderFieldName: kvp.KeyText));
            }

            if (ContainsAny(keySearch, TaxCodeKeywords))
            {
                var match = TaxCodeValuePattern().Match(kvp.ValueText).Value;
                Add(nameof(FieldName.SupplierTaxCode), string.IsNullOrEmpty(match) ? kvp.ValueText : match);
            }
            else if (ContainsAny(keySearch, SupplierKeywords))
            {
                Add(nameof(FieldName.SupplierName), CleanValue(kvp.ValueText));
            }
            else if (ContainsAny(keySearch, InvoiceDateKeywords))
            {
                var match = DateValuePattern().Match(kvp.ValueText).Value;
                Add(nameof(FieldName.InvoiceDate), string.IsNullOrEmpty(match) ? kvp.ValueText : match);
            }
            else if (!keySearch.Contains("in words", StringComparison.Ordinal)
                     && GetAmountFieldName(keySearch) is { } amountField)
            {
                // A spelled-out "total in words" value has no digits — LastMoneyValue correctly
                // returns null for it, and that must not fall back to the raw text (previously
                // `?? kvp.ValueText`), or a money field ends up holding a non-numeric string like
                // "seven hundred and thirt-".
                Add(amountField, LastMoneyValue(kvp.ValueText));
            }
            else if (ContainsAny(keySearch, InvoiceNumberKeywords))
            {
                var match = InvoiceNumberValuePattern().Match(kvp.ValueText).Value;
                Add(nameof(FieldName.InvoiceNumber), string.IsNullOrEmpty(match) ? kvp.ValueText : match);
            }
            else if (ContainsAny(keySearch, NotesKeywords))
            {
                Add(nameof(FieldName.Notes), CleanValue(kvp.ValueText));
            }
        }
    }

    /// <summary>
    /// Regex fallback scoped to individual layout paragraphs rather than the whole concatenated
    /// document text. Slightly more reliable than <see cref="AddFullTextFallbackCandidates"/>
    /// since a paragraph boundary can't accidentally splice together unrelated lines, and it
    /// carries real page/bounding-box context that the flat full-text pass cannot.
    /// </summary>
    private static void AddParagraphFallbackCandidates(NormalizedOcrDocument ocrResult, List<FieldCandidate> candidates)
    {
        foreach (var paragraph in ocrResult.Paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph.Text)) continue;

            AddParagraphRegexCandidate(candidates, nameof(FieldName.SupplierTaxCode), paragraph, TaxCodeFallbackPattern(), 0.68);
            AddParagraphRegexCandidate(candidates, nameof(FieldName.InvoiceNumber), paragraph, InvoiceNumberFallbackPattern(), 0.66);
            AddParagraphRegexCandidate(candidates, nameof(FieldName.InvoiceDate), paragraph, DateValuePattern(), 0.64);
            AddParagraphRegexCandidate(candidates, nameof(FieldName.TotalAmount), paragraph, TotalAmountFallbackPattern(), 0.66, useLastGroup: true);
            AddParagraphRegexCandidate(candidates, nameof(FieldName.VatAmount), paragraph, VatAmountFallbackPattern(), 0.64, useLastGroup: true);
            AddParagraphRegexCandidate(candidates, nameof(FieldName.SubtotalAmount), paragraph, SubtotalFallbackPattern(), 0.64, useLastGroup: true);
        }
    }

    private static void AddParagraphRegexCandidate(
        List<FieldCandidate> candidates,
        string fieldName,
        OcrParagraph paragraph,
        Regex pattern,
        double confidence,
        bool useLastGroup = false)
    {
        var match = pattern.Match(paragraph.Text);
        if (!match.Success) return;

        var value = useLastGroup
            ? match.Groups[^1].Value
            : match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;

        candidates.Add(new FieldCandidate(
            fieldName, value, confidence, paragraph.PageNumber, paragraph.BoundingBox,
            SourcePriority: 15, SourceMethod: "ParagraphRegex", SourceText: match.Value,
            SourceType: "Regex"));
    }

    /// <summary>
    /// Scans layout tables for a row whose label cell contains an amount keyword (e.g. "Tổng
    /// cộng") and whose other cells contain a money value — a common shape for the totals
    /// footer of a receipt/invoice table. Rows closer to the bottom of the table score higher,
    /// since a genuine grand total sits below the line-item rows.
    /// </summary>
    private static void AddTableFooterCandidates(NormalizedOcrDocument ocrResult, List<FieldCandidate> candidates)
    {
        foreach (var table in ocrResult.Tables)
        {
            if (table.Cells.Count == 0) continue;

            var rows = table.Cells.GroupBy(c => c.RowIndex).OrderBy(g => g.Key).ToList();

            foreach (var row in rows)
            {
                var labelCell = row.FirstOrDefault(c => GetAmountFieldName(NormalizeForSearch(c.Text)) is not null);
                if (labelCell is null) continue;

                var fieldName = GetAmountFieldName(NormalizeForSearch(labelCell.Text))!;

                // The amount is usually in a different cell on the same row — prefer the
                // right-most cell that actually contains a numeric money value.
                var valueCell = row
                    .Where(c => !ReferenceEquals(c, labelCell))
                    .OrderByDescending(c => c.ColumnIndex)
                    .FirstOrDefault(c => LastMoneyValue(c.Text) is not null);

                var value = LastMoneyValue(valueCell?.Text ?? labelCell.Text);
                if (string.IsNullOrWhiteSpace(value)) continue;

                var rowPositionBonus = rows.Count > 1 ? row.Key / (double)(rows.Count - 1) * 0.05 : 0;
                var baseScore = fieldName == nameof(FieldName.TotalAmount) ? 0.8 : 0.76;

                candidates.Add(new FieldCandidate(
                    fieldName,
                    value,
                    Math.Min(0.9, baseScore + rowPositionBonus),
                    table.PageNumber,
                    (valueCell ?? labelCell).BoundingBox,
                    SourcePriority: 40,
                    SourceMethod: "TableFooter",
                    SourceText: $"{labelCell.Text} | {(valueCell ?? labelCell).Text}",
                    SourceType: "Table"));
            }
        }
    }

    private static IReadOnlyList<LineContext> BuildLineContexts(NormalizedOcrDocument ocrResult)
    {
        var lines = ocrResult.Pages
            .SelectMany(page => page.Lines.Select(line => new LineContext(
                line.Text,
                NormalizeForSearch(line.Text),
                page.PageNumber,
                line.Confidence ?? page.Confidence ?? ocrResult.AverageConfidence,
                line.BoundingBox)))
            .Where(line => !string.IsNullOrWhiteSpace(line.Text))
            .ToList();

        if (lines.Count > 0) return lines;

        return SplitLines(ocrResult.FullText)
            .Select(line => new LineContext(line, NormalizeForSearch(line), 1, ocrResult.AverageConfidence, null))
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
            AddDueDateLineCandidate(lines, candidates, i);
            AddPoNumberLineCandidate(lines, candidates, i);
            AddAmountLineCandidates(lines, candidates, i);
            AddVatRateLineCandidate(lines, candidates, i);
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
                    SourcePriority: 30,
                    SourceMethod: "SupplierMarker",
                    SourceText: line.Text));
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
            SourcePriority: 60,
            SourceMethod: "LineKeyword",
            SourceText: line.Text));
    }

    /// <summary>
    /// POS/sales receipts often print the merchant name as the very first line with no label
    /// at all (e.g. "MOTA CAFE"). This scans the first few lines for the first one that isn't
    /// an address, phone number, date, invoice number, or document-type title, and uses it as
    /// a low-priority SupplierName candidate — only relevant when no labeled candidate exists.
    /// </summary>
    private static void AddSupplierNameTopLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates)
    {
        var scanCount = Math.Min(lines.Count, TopLineSupplierScanWindow);
        for (var i = 0; i < scanCount; i++)
        {
            var line = lines[i];
            if (!IsLikelySupplierNameLine(line)) continue;

            candidates.Add(new FieldCandidate(
                nameof(FieldName.SupplierName),
                CleanValue(line.Text),
                Score(line, 0.68),
                line.PageNumber,
                line.BoundingBox,
                SourcePriority: 20,
                SourceMethod: "SupplierTopLine",
                SourceText: line.Text));
            return;
        }
    }

    /// <summary>
    /// International-invoice labels (Bill To/Ship To/Tax) that never name a vendor even though
    /// they can share the first few lines with the real vendor name — checked alongside the
    /// Vietnamese exclusion keywords below rather than replacing them.
    /// </summary>
    private static readonly string[] VendorNameExclusionKeywords = ["bill to", "ship to", "tax"];

    private static bool IsLikelySupplierNameLine(LineContext line)
    {
        var text = line.Text.Trim();
        if (text.Length < 3) return false;
        if (!text.Any(char.IsLetter)) return false;

        if (ContainsAny(line.SearchText, TaxCodeKeywords)) return false;
        if (ContainsAny(line.SearchText, InvoiceNumberKeywords)) return false;
        if (ContainsAny(line.SearchText, InvoiceDateKeywords)) return false;
        if (ContainsAny(line.SearchText, DueDateKeywords)) return false;
        if (ContainsAny(line.SearchText, PoNumberKeywords)) return false;
        if (ContainsAny(line.SearchText, SupplierKeywords)) return false;
        if (ContainsAny(line.SearchText, NotesKeywords)) return false;
        if (ContainsAny(line.SearchText, AddressMarkers)) return false;
        if (ContainsAny(line.SearchText, VendorNameExclusionKeywords)) return false;
        if (GetAmountFieldName(line.SearchText) is not null) return false;

        if (InvoiceDocumentPattern().IsMatch(text) || ReceiptDocumentPattern().IsMatch(text)) return false;
        if (DateValuePattern().IsMatch(text)) return false;
        if (char.IsDigit(text[0])) return false;

        var digitCount = text.Count(char.IsDigit);
        var significantCount = text.Count(c => !char.IsWhiteSpace(c));
        if (significantCount > 0 && digitCount / (double)significantCount > 0.5) return false;

        return true;
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
            SourcePriority: 70,
            SourceMethod: "LineKeyword",
            SourceText: line.Text));
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
            SourcePriority: 55,
            SourceMethod: "LineKeyword",
            SourceText: line.Text));
    }

    private static void AddInvoiceDateLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        if (!ContainsAny(line.SearchText, InvoiceDateKeywords)) return;
        // "Due Date:" is a distinct field (see AddDueDateLineCandidate) — the bare "date"
        // keyword above must not also claim it as the invoice date.
        if (ContainsAny(line.SearchText, DueDateKeywords)) return;

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
            SourcePriority: 55,
            SourceMethod: "LineKeyword",
            SourceText: line.Text));
    }

    /// <summary>International invoice "Due Date:" — a distinct field key ("DueDate", not part of the legacy <see cref="FieldName"/> enum) matching <c>DocumentProfileCatalog</c>'s international invoice profile.</summary>
    private static void AddDueDateLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        if (!ContainsAny(line.SearchText, DueDateKeywords)) return;

        var value = DateValuePattern().Match(line.Text).Value;
        if (string.IsNullOrWhiteSpace(value) && TryGetNearbyLine(lines, index, out var nearby))
        {
            value = DateValuePattern().Match(nearby.Text).Value;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        candidates.Add(new FieldCandidate(
            "DueDate",
            value,
            Score(line, 0.86),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 55,
            SourceMethod: "LineKeyword",
            SourceText: line.Text));
    }

    /// <summary>International invoice "PO Number:"/"Purchase Order:" — a distinct field key ("PONumber") matching <c>DocumentProfileCatalog</c>'s international invoice profile. PO numbers can be short (e.g. "35"), so the value is whatever follows the label rather than the stricter <see cref="InvoiceNumberValuePattern"/>.</summary>
    private static void AddPoNumberLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        if (!ContainsAny(line.SearchText, PoNumberKeywords)) return;

        var value = ValueAfterLabel(line.Text);
        if (string.IsNullOrWhiteSpace(value) && TryGetNearbyLine(lines, index, out var nearby))
        {
            value = nearby.Text;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        candidates.Add(new FieldCandidate(
            "PONumber",
            CleanValue(value),
            Score(line, 0.84),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 55,
            SourceMethod: "LineKeyword",
            SourceText: line.Text));
    }

    private static void AddAmountLineCandidates(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        // A spelled-out "total in words" line is not the numeric total — letting it through
        // could otherwise outrank the real "TOTAL: 734.33 EUR" line's genuine numeric value.
        if (line.SearchText.Contains("in words", StringComparison.Ordinal)) return;

        var fieldName = GetAmountFieldName(line.SearchText);
        if (fieldName is null) return;

        var value = LastMoneyValue(line.Text);
        var sourceLine = line;
        if (string.IsNullOrWhiteSpace(value) && TryGetNearbyLine(lines, index, out var nearby))
        {
            value = LastMoneyValue(nearby.Text);
            sourceLine = nearby;
        }

        if (string.IsNullOrWhiteSpace(value)) return;

        var baseScore = fieldName == nameof(FieldName.TotalAmount) ? 0.88 : 0.84;
        if (fieldName == nameof(FieldName.TotalAmount))
        {
            var weight = GetTotalKeywordWeight(line.SearchText);
            var keywordBonus = (weight - 1) * 0.03;
            var positionBonus = lines.Count > 1 ? index / (double)(lines.Count - 1) * 0.05 : 0;
            baseScore = Math.Min(0.97, baseScore + keywordBonus + positionBonus);
        }

        candidates.Add(new FieldCandidate(
            fieldName,
            value,
            Score(line, baseScore),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 65,
            SourceMethod: "LineKeyword",
            SourceText: sourceLine.Text));
    }

    /// <summary>
    /// Best-effort single VAT-rate candidate ("VatRate" — profile-only key, no <see cref="FieldName"/>
    /// enum value, same pattern as "DueDate"/"PONumber") for a line that mentions VAT/GTGT and
    /// carries a rate marker ("10%", "KCT", "KKKNT"). Deliberately narrow: real per-line-item VAT
    /// rate extraction is a materially larger effort (see docs/decisions.md); this only lets
    /// <c>DocumentProcessingService</c> synthesize a single tax-breakdown row when a document-level
    /// rate is unambiguous in the text, mirroring what TT78 XML always gets from THTTLTSuat.
    /// </summary>
    private static void AddVatRateLineCandidate(
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates,
        int index)
    {
        var line = lines[index];
        if (!ContainsAny(line.SearchText, VatKeywords)) return;

        var match = VatRateMarkerPattern().Match(line.Text);
        if (!match.Success) return;

        candidates.Add(new FieldCandidate(
            "VatRate",
            match.Value,
            Score(line, 0.8),
            line.PageNumber,
            line.BoundingBox,
            SourcePriority: 60,
            SourceMethod: "LineKeyword",
            SourceText: line.Text));
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
            SourcePriority: 35,
            SourceMethod: "LineKeyword",
            SourceText: line.Text));
    }

    private static void AddFullTextFallbackCandidates(NormalizedOcrDocument ocrResult, List<FieldCandidate> candidates)
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

        candidates.Add(new FieldCandidate(
            fieldName, value, confidence, null, null,
            SourcePriority: 10,
            SourceMethod: "FullTextRegex",
            SourceText: match.Value,
            SourceType: "Regex"));
    }

    private static void AddDocumentTypeCandidate(
        NormalizedOcrDocument ocrResult,
        IReadOnlyList<LineContext> lines,
        List<FieldCandidate> candidates)
    {
        var searchText = NormalizeForSearch(GetFullText(ocrResult));
        if (string.IsNullOrWhiteSpace(searchText))
        {
            searchText = string.Join('\n', lines.Select(l => l.SearchText));
        }

        var originalText = GetFullText(ocrResult);

        // Most-specific category first: a restaurant/POS bill also matches the generic
        // Receipt/Invoice patterns below, so it must be checked before them. This value feeds
        // both the legacy DocumentType enum (via Enum.TryParse in DocumentProcessingService,
        // which simply ignores values it doesn't recognize) and the richer DocumentCategory
        // enum used by the dynamic review profile system (IDocumentProfileCatalog.ResolveCategory
        // tries DocumentCategory first) — so this string can be a value from either enum.
        string? documentType = null;
        if (AppReceiptMarkerPattern().IsMatch(originalText) || searchText.Contains("ma don hang", StringComparison.Ordinal))
        {
            // Screenshots of delivery/order-app receipts (Grab, ShopeeFood, Baemin, ...) use
            // very particular vocabulary that would otherwise fall through to the generic
            // Receipt/Invoice buckets below.
            documentType = nameof(DocumentCategory.AppReceiptScreenshot);
        }
        else if (RestaurantMarkerPattern().IsMatch(originalText))
        {
            documentType = nameof(DocumentType.RestaurantBill);
        }
        else if (searchText.Contains("hoa don ban hang", StringComparison.Ordinal))
        {
            // "Hóa đơn bán hàng" is the printed title of a POS/sales receipt, not a VAT
            // invoice — classify it as PosReceipt even though it contains the substring
            // "hóa đơn", so SupplierTaxCode validation does not wrongly require a tax code.
            documentType = nameof(DocumentType.PosReceipt);
        }
        else if (ReceiptDocumentPattern().IsMatch(originalText)
                 || searchText.Contains("bien lai", StringComparison.Ordinal)
                 || searchText.Contains("phieu thu", StringComparison.Ordinal)
                 || searchText.Contains("receipt", StringComparison.Ordinal))
        {
            documentType = nameof(DocumentType.Receipt);
        }
        else if (searchText.Contains("gia tri gia tang", StringComparison.Ordinal))
        {
            // "Hóa đơn giá trị gia tăng" is the formal Vietnamese VAT invoice title —
            // SupplierTaxCode is required for it.
            documentType = nameof(DocumentType.VatInvoice);
        }
        else if (CommercialInvoiceMarkerPattern().IsMatch(originalText))
        {
            documentType = nameof(DocumentCategory.CommercialInvoice);
        }
        else if (InternationalInvoiceMarkerPattern().IsMatch(originalText))
        {
            // English-language "tax invoice" / "bill to" / "purchase order" phrasing indicates
            // an international/commercial-style invoice rather than a Vietnamese one — checked
            // before the generic "invoice" fallback below, which would otherwise catch it first.
            documentType = nameof(DocumentCategory.InternationalInvoice);
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
            SourcePriority: 45,
            SourceMethod: "DocumentTypeHeuristic",
            SourceText: documentType,
            SourceType: "Heuristic"));
    }

    private static void AddCurrencyCandidate(
        NormalizedOcrDocument ocrResult,
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
            SourcePriority: 35,
            SourceMethod: "CurrencyHeuristic",
            SourceText: line.Text,
            SourceType: "Heuristic"));
    }

    /// <summary>
    /// Vietnamese invoices/receipts frequently omit any currency marker at all — the grouped
    /// dot-thousands money format itself is the signal. Only fires when no explicit currency
    /// candidate was found, and never outranks one (lowest possible priority/confidence).
    /// </summary>
    private static void AddDefaultCurrencyCandidate(List<FieldCandidate> candidates)
    {
        if (candidates.Any(c => c.FieldName == nameof(FieldName.Currency))) return;

        var hasVietnameseMoneyFormat = candidates
            .Where(c => c.FieldName is nameof(FieldName.SubtotalAmount)
                or nameof(FieldName.VatAmount)
                or nameof(FieldName.TotalAmount))
            .Any(c => c.RawValue is not null && VietnameseGroupedMoneyPattern().IsMatch(c.RawValue));

        if (!hasVietnameseMoneyFormat) return;

        candidates.Add(new FieldCandidate(
            nameof(FieldName.Currency),
            "VND",
            0.5,
            null,
            null,
            SourcePriority: 5,
            SourceMethod: "CurrencyDefault",
            SourceText: null,
            SourceType: "Heuristic"));
    }

    private static string? GetAmountFieldName(string searchText)
    {
        if (ContainsAny(searchText, VatKeywords)) return nameof(FieldName.VatAmount);
        if (ContainsAny(searchText, SubtotalKeywords)) return nameof(FieldName.SubtotalAmount);
        if (TotalKeywordWeights.Any(k => searchText.Contains(k.Keyword, StringComparison.Ordinal))) return nameof(FieldName.TotalAmount);

        return null;
    }

    private static int GetTotalKeywordWeight(string searchText)
    {
        var best = 0;
        foreach (var (keyword, weight) in TotalKeywordWeights)
        {
            if (searchText.Contains(keyword, StringComparison.Ordinal) && weight > best)
            {
                best = weight;
            }
        }

        return best;
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

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null) return null;
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string GetFullText(NormalizedOcrDocument ocrResult)
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
        int SourcePriority,
        string SourceMethod,
        string? SourceText,
        string SourceType = "Line",
        string? ProviderFieldName = null);

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

    // The last alternative (\d{1,2}[-\s][A-Za-z]{3,9}[-\s]\d{2,4}) covers English "dd-MMM-yyyy"
    // / "dd MMMM yyyy" style dates (e.g. "20-Mar-2008") common on international invoices —
    // DateTime.TryParse's culture-aware fallback in FieldNormalizationService.NormalizeDate
    // already handles that shape once it's isolated from any surrounding label text like this.
    [GeneratedRegex(@"\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{1,2}\.\d{1,2}\.\d{2,4}|\d{4}-\d{1,2}-\d{1,2}|\d{1,2}[-\s][A-Za-z]{3,9}[-\s]\d{2,4}")]
    private static partial Regex DateValuePattern();

    [GeneratedRegex(@"(?:VND|VNĐ|₫|đ)?\s*\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyValuePattern();

    [GeneratedRegex(@"^\d{1,3}(?:\.\d{3})+$")]
    private static partial Regex VietnameseGroupedMoneyPattern();

    [GeneratedRegex(@"\d{1,2}(?:[.,]\d+)?\s*%|\bKCT\b|\bKKKNT\b", RegexOptions.IgnoreCase)]
    private static partial Regex VatRateMarkerPattern();

    [GeneratedRegex(@"(?:VND|VNĐ|₫|đồng|dong|EUR|USD|GBP|JPY|CNY|€|£|\$)", RegexOptions.IgnoreCase)]
    private static partial Regex CurrencyMarkerPattern();

    /// <summary>URL/domain shape — an <see cref="InvoiceNumberValuePattern"/> match this loose can otherwise swallow a website line whole (e.g. a normalized search text containing "so" as a false-positive substring match of an unrelated word).</summary>
    [GeneratedRegex(@"^(https?://|www\.)|\.[a-z]{2,4}(/\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlLikePattern();

    [GeneratedRegex(@"(?:HÓA\s*ĐƠN|HOA\s*DON|Invoice)", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceDocumentPattern();

    [GeneratedRegex(@"(?:BIÊN\s*LAI|BIEN\s*LAI|PHIẾU\s*THU|PHIEU\s*THU|Receipt)", RegexOptions.IgnoreCase)]
    private static partial Regex ReceiptDocumentPattern();

    [GeneratedRegex(@"(?:NHÀ\s*HÀNG|NHA\s*HANG|QUÁN\s*ĂN|QUAN\s*AN|Restaurant)", RegexOptions.IgnoreCase)]
    private static partial Regex RestaurantMarkerPattern();

    [GeneratedRegex(@"(?:Mã\s*đơn\s*hàng|Order\s*ID|ShopeeFood|GrabFood|Baemin|Now\.vn)", RegexOptions.IgnoreCase)]
    private static partial Regex AppReceiptMarkerPattern();

    [GeneratedRegex(@"Commercial\s*Invoice", RegexOptions.IgnoreCase)]
    private static partial Regex CommercialInvoiceMarkerPattern();

    [GeneratedRegex(@"(?:Tax\s*Invoice|Bill\s*To|Purchase\s*Order|PO\s*Number)", RegexOptions.IgnoreCase)]
    private static partial Regex InternationalInvoiceMarkerPattern();

    [GeneratedRegex(@"(?:Mã\s*số\s*thuế|MST|Tax\s*code)\s*[:\-]?\s*([0-9][0-9\s\-.]{8,18})", RegexOptions.IgnoreCase)]
    private static partial Regex TaxCodeFallbackPattern();

    [GeneratedRegex(@"(?:Số\s*hóa\s*đơn|Số\s*HĐ|Invoice\s*(?:No|Number)|Số)\s*[:\-]?\s*([A-Z0-9][A-Z0-9\-/.]{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex InvoiceNumberFallbackPattern();

    [GeneratedRegex(@"(?:Tổng\s*thanh\s*toán|Tổng\s*cộng(?:\s*tiền\s*thanh\s*toán)?|Tổng\s*tiền|Tổng)\s*[:\-]?\s*((?:VND|VNĐ|₫|đ)?\s*\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?)", RegexOptions.IgnoreCase)]
    private static partial Regex TotalAmountFallbackPattern();

    [GeneratedRegex(@"(?:VAT|Thuế\s*GTGT)\D*((?:VND|VNĐ|₫|đ)?\s*\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?)", RegexOptions.IgnoreCase)]
    private static partial Regex VatAmountFallbackPattern();

    [GeneratedRegex(@"(?:Cộng\s*tiền\s*hàng|Subtotal|Thành\s*tiền\s*chưa\s*thuế|Thành\s*tiền|Tiền\s*hàng)\s*[:\-]?\s*((?:VND|VNĐ|₫|đ)?\s*\d+(?:[.,\s]\d{3})*(?:[.,]\d+)?\s*(?:VND|VNĐ|₫|đ)?)", RegexOptions.IgnoreCase)]
    private static partial Regex SubtotalFallbackPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
