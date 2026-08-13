using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentOCR.Application.Credits;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DocumentOCR.Application.Processing;

/// <summary>
/// Parses Vietnamese TT78 (Thông tư 78/2021/TT-BTC) e-invoice XML directly into
/// <see cref="ExtractedField"/>s with full confidence, bypassing OCR and
/// <see cref="IFieldExtractionService"/> entirely — see
/// <c>docs/decisions.md</c> for the assumed XML structure and the rationale for this seam.
/// </summary>
public class TT78XmlInvoiceParser : IStructuredInvoiceParser
{
    private const string ProviderFieldSource = "TT78Xml";

    /// <summary>Suffix appended to <see cref="ExtractedField.ExtractionMethod"/> when a value had to fall back to a "TTKhac" (provider extension) tag instead of the TT78 standard tag — see <see cref="ResolveMoneyValue"/>.</summary>
    private const string ExtensionFallbackSuffix = ":Extension";

    /// <summary><see cref="ExtractedField.ExtractionMethod"/> used for <see cref="FieldName.InvoiceNature"/> when no "TCHDon" tag is present and the value is a guessed default, not a read one.</summary>
    private const string InferredDefaultMethod = ProviderFieldSource + ":InferredDefault";

    // VAT-rate canonicalization is pure/stateless — instantiated directly rather than injected,
    // since this parser is registered as a DI singleton and IFieldNormalizationService is scoped.
    private static readonly FieldNormalizationService NormalizationHelper = new();

    // Extension ("TTKhac") field-name synonyms observed on real MISA-issued invoices only used
    // when the corresponding TT78 standard tag is absent from <TToan> — see docs/decisions.md.
    // No synonym list exists for other fields (supplier/buyer/invoice-number/...): every real
    // sample seen so far always carries those as standard tags, so guessing TTKhac names for them
    // would be speculative rather than evidence-based.
    private static readonly string[] SubtotalExtensionNames = ["TotalAmountWithoutVAT", "TotalSaleAmount"];
    private static readonly string[] VatExtensionNames = ["TotalVATAmount"];
    private static readonly string[] TotalExtensionNames = ["TotalAmount"];

    private readonly ILogger<TT78XmlInvoiceParser> _logger;

    public TT78XmlInvoiceParser(ILogger<TT78XmlInvoiceParser>? logger = null)
    {
        _logger = logger ?? NullLogger<TT78XmlInvoiceParser>.Instance;
    }

    public bool CanParse(string contentType, string fileName)
    {
        var isXmlExtension = string.Equals(Path.GetExtension(fileName), ".xml", StringComparison.OrdinalIgnoreCase);
        var isXmlContentType = string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase);
        return isXmlExtension || isXmlContentType;
    }

    public async Task<StructuredExtractionResult> ParseAsync(Stream content, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var rawBytes = buffer.ToArray();
        var rawXml = Encoding.UTF8.GetString(rawBytes);

        XDocument document;
        try
        {
            using var bufferStream = new MemoryStream(rawBytes);
            using var reader = XmlReader.Create(bufferStream, SafeXmlReaderSettings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            return new StructuredExtractionResult
            {
                Success = false,
                ErrorMessage = $"File is not well-formed XML: {ex.Message}",
                RawSourceText = rawXml,
                ProviderName = CreditPricing.StructuredXmlProviderName,
                PageCount = 1
            };
        }

        // "DLHDon" (invoice data block) may be wrapped in a digital-signature envelope; find it
        // anywhere in the tree by local name (ignoring namespace/prefix), but never inside a
        // "Signature" (XML-DSig) subtree.
        var dataRoot = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "DLHDon"
                && !e.AncestorsAndSelf().Any(a => a.Name.LocalName == "Signature"));

        if (dataRoot is null)
        {
            return new StructuredExtractionResult
            {
                Success = false,
                ErrorMessage = "Could not find invoice data (DLHDon) in the XML file.",
                RawSourceText = rawXml,
                ProviderName = CreditPricing.StructuredXmlProviderName,
                PageCount = 1
            };
        }

        var generalInfo = FindDescendant(dataRoot, "TTChung");
        var seller = FindDescendant(dataRoot, "NBan");
        var buyer = FindDescendant(dataRoot, "NMua");
        var totals = FindDescendant(dataRoot, "TToan");

        // "MCCQT" (mã của cơ quan thuế) is a sibling of DLHDon at the HDon root, stamped by the
        // tax authority once it receives the invoice — not part of the invoice content itself.
        var taxAuthorityCode = document.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "MCCQT"
                && !e.AncestorsAndSelf().Any(a => a.Name.LocalName == "Signature"))
            ?.Value?.Trim();

        var invoiceNumber = GetChildValue(generalInfo, "SHDon");
        var templateCode = GetChildValue(generalInfo, "KHMSHDon");
        var serial = GetChildValue(generalInfo, "KHHDon");
        var invoiceDate = GetChildValue(generalInfo, "NLap");
        var currency = GetChildValue(generalInfo, "DVTTe");
        // "TCHDon" (tính chất hoá đơn) — when absent, InvoiceNature is added further below as an
        // inferred "hoá đơn gốc" default rather than left unread; see NormalizeInvoiceNature.
        var invoiceNatureCode = GetChildValue(generalInfo, "TCHDon");

        var supplierTaxCode = GetChildValue(seller, "MST");
        var supplierName = GetChildValue(seller, "Ten");
        var supplierAddress = GetChildValue(seller, "DChi");

        var buyerTaxCode = GetChildValue(buyer, "MST");
        var buyerName = GetChildValue(buyer, "Ten");
        var buyerAddress = GetChildValue(buyer, "DChi");

        // TT78 standard tags (direct children of <TToan>) are always tried first; only when a
        // standard tag is absent do we fall back to the provider-specific "TTKhac" extension
        // block — see docs/decisions.md and ResolveMoneyValue.
        var subtotalResult = ResolveMoneyValue(totals, "TgTCThue", SubtotalExtensionNames, nameof(FieldName.SubtotalAmount));
        var vatResult = ResolveMoneyValue(totals, "TgTThue", VatExtensionNames, nameof(FieldName.VatAmount));
        var totalResult = ResolveMoneyValue(totals, "TgTTTBSo", TotalExtensionNames, nameof(FieldName.TotalAmount));
        var paymentMethod = GetChildValue(generalInfo, "HTTToan");
        var amountInWords = GetChildValue(totals, "TgTTTBChu");

        // "Mã tra cứu" (lookup code, shown on the printed PDF and on the issuer's public
        // lookup portal) is DLHDon's own "Id" attribute — the same identifier the XML-DSig
        // signature references (<Reference URI="#...">), so it's present on any signed invoice.
        var lookupCodeRaw = dataRoot.Attribute("Id")?.Value?.Trim();
        var lookupCode = string.IsNullOrWhiteSpace(lookupCodeRaw) ? null : lookupCodeRaw;

        var fields = new List<ExtractedField>();

        AddField(fields, FieldName.InvoiceNumber, invoiceNumber, "SHDon");
        AddField(fields, FieldName.InvoiceDate, invoiceDate, "NLap");
        AddField(fields, FieldName.SupplierTaxCode, supplierTaxCode, "MST");
        AddField(fields, FieldName.SupplierName, supplierName, "Ten");
        AddField(fields, FieldName.SupplierAddress, supplierAddress, "DChi");
        AddField(fields, FieldName.BuyerTaxCode, buyerTaxCode, "MST");
        AddField(fields, FieldName.BuyerName, buyerName, "Ten");
        AddField(fields, FieldName.BuyerAddress, buyerAddress, "DChi");
        AddField(fields, FieldName.InvoiceForm, templateCode, "KHMSHDon");
        AddField(fields, FieldName.InvoiceSymbol, serial, "KHHDon");
        AddField(fields, FieldName.TaxAuthorityCode, taxAuthorityCode, "MCCQT");
        AddInvoiceNatureField(fields, invoiceNatureCode);
        AddMoneyField(fields, FieldName.SubtotalAmount, subtotalResult);
        AddMoneyField(fields, FieldName.VatAmount, vatResult);
        AddMoneyField(fields, FieldName.TotalAmount, totalResult);
        // Profile-only field keys (see DocumentProfileCatalog's "amounts"/"invoice" sections) —
        // no dedicated FieldName enum value, so written directly by string key like the OCR
        // pipeline's "VatRate"/"DueDate"/"PONumber" candidates.
        AddField(fields, "PaymentMethod", paymentMethod, "HTTToan");
        AddField(fields, "AmountInWords", amountInWords, "TgTTTBChu");
        AddField(fields, "LookupCode", lookupCode, "Id");

        if (!string.IsNullOrWhiteSpace(currency))
            AddField(fields, FieldName.Currency, currency, "DVTTe");
        else
            AddField(fields, FieldName.Currency, "VND", providerFieldName: null);

        // TT78 XML is definitionally a formal VAT invoice — synthesized, not read from a tag, so
        // downstream document-type detection (DocumentProcessingService.GetDetectedDocumentType)
        // resolves the correct review profile instead of falling back to Unknown.
        AddField(fields, FieldName.DocumentType, nameof(DocumentType.VatInvoice), providerFieldName: null);

        var taxBreakdown = ParseTaxBreakdown(totals);

        return new StructuredExtractionResult
        {
            Success = true,
            Fields = fields,
            TaxBreakdown = taxBreakdown,
            RawSourceText = rawXml,
            InvoiceTemplateCode = templateCode,
            InvoiceSerial = serial,
            ProviderName = CreditPricing.StructuredXmlProviderName,
            PageCount = 1
        };
    }

    /// <summary>
    /// Reads every "THTTLTSuat/LTSuat" line (one per distinct VAT rate on the invoice) — even a
    /// single-rate invoice always has exactly one "LTSuat" element, so this naturally produces
    /// exactly one row for the common case with no special-casing needed.
    /// </summary>
    private static List<InvoiceTaxBreakdown> ParseTaxBreakdown(XElement? totals)
    {
        var breakdownRoot = totals is null ? null : FindDescendant(totals, "THTTLTSuat");
        if (breakdownRoot is null) return [];

        var lines = breakdownRoot.Elements().Where(e => e.Name.LocalName == "LTSuat").ToList();
        var result = new List<InvoiceTaxBreakdown>(lines.Count);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var rawRate = GetChildValue(line, "TSuat");
            var taxableAmount = GetChildValue(line, "ThTien");
            var taxAmount = GetChildValue(line, "TThue");

            result.Add(new InvoiceTaxBreakdown
            {
                RawVatRate = rawRate,
                VatRate = NormalizationHelper.NormalizeVatRate(rawRate),
                TaxableAmount = TryParseInvariantMoney(taxableAmount, out var taxable) ? taxable : null,
                TaxAmount = TryParseInvariantMoney(taxAmount, out var tax) ? tax : null,
                Confidence = 1.0,
                SortOrder = i
            });
        }

        return result;
    }

    /// <summary>
    /// Numeric-code → label map for "TCHDon" (tính chất hoá đơn): 1=gốc, 2=thay thế, 3=điều
    /// chỉnh, 4=huỷ. Passes any other value (including known codes outside this range, e.g. "5"
    /// for chiết khấu thương mại per Quyết định 1306/QĐ-CT) through unchanged so it stays visible
    /// to the user rather than being silently dropped or misclassified.
    /// </summary>
    private static string NormalizeInvoiceNature(string rawValue) =>
        rawValue.Trim() switch
        {
            "1" => "Hóa đơn gốc",
            "2" => "Hóa đơn thay thế",
            "3" => "Hóa đơn điều chỉnh",
            "4" => "Hóa đơn huỷ",
            var other => other
        };

    private static readonly XmlReaderSettings SafeXmlReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null
    };

    private static XElement? FindDescendant(XElement parent, string localName) =>
        parent.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);

    private static string? GetChildValue(XElement? parent, string localName)
    {
        var value = parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void AddField(
        List<ExtractedField> fields, FieldName fieldName, string? rawValue, string? providerFieldName) =>
        AddField(fields, fieldName.ToString(), rawValue, providerFieldName);

    private static void AddField(
        List<ExtractedField> fields, string fieldName, string? rawValue, string? providerFieldName)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return;

        fields.Add(new ExtractedField
        {
            FieldName = fieldName,
            RawValue = rawValue,
            Confidence = 1.0,
            SourceType = ProviderFieldSource,
            ExtractionMethod = ProviderFieldSource,
            ProviderFieldName = providerFieldName
        });
    }

    /// <summary>
    /// Adds <see cref="FieldName.InvoiceNature"/> from the "TCHDon" tag when present. When absent,
    /// still adds the field defaulted to "Hóa đơn gốc" (code 1) rather than leaving it unread, but
    /// at reduced confidence and a distinct <see cref="ExtractedField.ExtractionMethod"/>
    /// (<see cref="InferredDefaultMethod"/>) so it's clearly a guess, not a value read from the
    /// XML — the reduced confidence also falls under FieldValidationService's low-confidence
    /// threshold (0.75), so the existing LOW_CONFIDENCE warning surfaces it for the user to confirm.
    /// </summary>
    private static void AddInvoiceNatureField(List<ExtractedField> fields, string? rawValue)
    {
        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            fields.Add(new ExtractedField
            {
                FieldName = nameof(FieldName.InvoiceNature),
                RawValue = rawValue,
                NormalizedValue = NormalizeInvoiceNature(rawValue),
                Confidence = 1.0,
                SourceType = ProviderFieldSource,
                ExtractionMethod = ProviderFieldSource,
                ProviderFieldName = "TCHDon"
            });
            return;
        }

        fields.Add(new ExtractedField
        {
            FieldName = nameof(FieldName.InvoiceNature),
            RawValue = null,
            NormalizedValue = NormalizeInvoiceNature("1"),
            Confidence = 0.5,
            SourceType = ProviderFieldSource,
            ExtractionMethod = InferredDefaultMethod,
            ProviderFieldName = null
        });
    }

    /// <summary>Result of resolving one money field via <see cref="ResolveMoneyValue"/>.</summary>
    private readonly record struct MoneyResolution(string? RawValue, string ProviderFieldName, bool IsExtension);

    /// <summary>
    /// TT78 standard tag first (direct child of <paramref name="totals"/>); only when that tag is
    /// absent or blank does this fall back to a matching "TTKhac/TTin" whose "TTruong" name is one
    /// of <paramref name="extensionNames"/> — provider-specific extension data (e.g. MISA's
    /// "TotalAmountWithoutVAT"), never the TT78 standard itself. Logs a Warning on fallback so a
    /// document relying on extension data is diagnosable later.
    /// </summary>
    private MoneyResolution ResolveMoneyValue(
        XElement? totals, string standardTag, IReadOnlyList<string> extensionNames, string fieldNameForLog)
    {
        var standardValue = GetChildValue(totals, standardTag);
        if (standardValue is not null)
            return new MoneyResolution(standardValue, standardTag, IsExtension: false);

        var ttKhac = totals?.Elements().FirstOrDefault(e => e.Name.LocalName == "TTKhac");
        if (ttKhac is null)
            return new MoneyResolution(null, standardTag, IsExtension: false);

        foreach (var ttin in ttKhac.Elements().Where(e => e.Name.LocalName == "TTin"))
        {
            var ttruong = ttin.Elements().FirstOrDefault(c => c.Name.LocalName == "TTruong")?.Value?.Trim();
            if (ttruong is null || !extensionNames.Contains(ttruong, StringComparer.OrdinalIgnoreCase))
                continue;

            var dLieu = ttin.Elements().FirstOrDefault(c => c.Name.LocalName == "DLieu")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(dLieu))
                continue;

            _logger.LogWarning(
                "TT78 XML: standard tag <{StandardTag}> missing for {FieldName}; falling back to TTKhac extension field {ExtensionFieldName}.",
                standardTag, fieldNameForLog, ttruong);

            return new MoneyResolution(dLieu, ttruong, IsExtension: true);
        }

        return new MoneyResolution(null, standardTag, IsExtension: false);
    }

    /// <summary>
    /// If the resolved value isn't a valid decimal, this is treated the same as it being absent:
    /// no field is added, rather than fabricating a zero value at full confidence. See
    /// <see cref="TryParseInvariantMoney"/> for the parsing rationale.
    /// </summary>
    private static void AddMoneyField(List<ExtractedField> fields, FieldName fieldName, MoneyResolution resolution)
    {
        if (!TryParseInvariantMoney(resolution.RawValue, out var value))
            return;

        fields.Add(new ExtractedField
        {
            FieldName = fieldName.ToString(),
            RawValue = resolution.RawValue,
            NormalizedValue = value.ToString("0.####################", CultureInfo.InvariantCulture),
            Confidence = 1.0,
            SourceType = ProviderFieldSource,
            ExtractionMethod = resolution.IsExtension ? ProviderFieldSource + ExtensionFallbackSuffix : ProviderFieldSource,
            ProviderFieldName = resolution.ProviderFieldName
        });
    }

    /// <summary>
    /// TT78 XML money values are machine-formatted decimals (e.g. "10019909.000000", a dot as the
    /// decimal point, no thousands grouping) — never OCR-style Vietnamese-formatted text. Parsing
    /// with <see cref="CultureInfo.InvariantCulture"/> directly, rather than letting
    /// <c>FieldNormalizationService.NormalizeCurrency</c>'s Vietnamese-text regex touch them,
    /// avoids that regex misreading a many-digit decimal fraction (its thousands-grouping logic
    /// assumes at most 3 fractional digits) as a separate, wrong trailing number.
    /// </summary>
    private static bool TryParseInvariantMoney(string? rawValue, out decimal value)
    {
        value = default;
        return !string.IsNullOrWhiteSpace(rawValue)
            && decimal.TryParse(rawValue, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
