using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

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

    // VAT-rate canonicalization is pure/stateless — instantiated directly rather than injected,
    // since this parser is registered as a DI singleton and IFieldNormalizationService is scoped.
    private static readonly FieldNormalizationService NormalizationHelper = new();

    public bool CanParse(string contentType, string fileName)
    {
        var isXmlExtension = string.Equals(Path.GetExtension(fileName), ".xml", StringComparison.OrdinalIgnoreCase);
        var isXmlContentType = string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase);
        return isXmlExtension || isXmlContentType;
    }

    public async Task<StructuredInvoiceResult> ParseAsync(Stream content, CancellationToken ct = default)
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
            return new StructuredInvoiceResult
            {
                Success = false,
                ErrorMessage = $"File is not well-formed XML: {ex.Message}",
                RawXml = rawXml
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
            return new StructuredInvoiceResult
            {
                Success = false,
                ErrorMessage = "Could not find invoice data (DLHDon) in the XML file.",
                RawXml = rawXml
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
        // Best-effort — "TCHDon" (tính chất hoá đơn) is not confirmed against a real sample that
        // carries it (see docs/decisions.md); absent in the fixture used to verify this parser.
        var invoiceNatureCode = GetChildValue(generalInfo, "TCHDon");

        var supplierTaxCode = GetChildValue(seller, "MST");
        var supplierName = GetChildValue(seller, "Ten");
        var supplierAddress = GetChildValue(seller, "DChi");

        var buyerTaxCode = GetChildValue(buyer, "MST");
        var buyerName = GetChildValue(buyer, "Ten");
        var buyerAddress = GetChildValue(buyer, "DChi");

        var subtotal = GetChildValue(totals, "TgTCThue");
        var vat = GetChildValue(totals, "TgTThue");
        var total = GetChildValue(totals, "TgTTTBSo");
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
        AddField(fields, FieldName.InvoiceNature, NormalizeInvoiceNature(invoiceNatureCode), "TCHDon");
        AddMoneyField(fields, FieldName.SubtotalAmount, subtotal, "TgTCThue");
        AddMoneyField(fields, FieldName.VatAmount, vat, "TgTThue");
        AddMoneyField(fields, FieldName.TotalAmount, total, "TgTTTBSo");
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

        return new StructuredInvoiceResult
        {
            Success = true,
            Fields = fields,
            TaxBreakdown = taxBreakdown,
            RawXml = rawXml,
            InvoiceTemplateCode = templateCode,
            InvoiceSerial = serial
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
    /// Best-effort numeric-code → label map for "TCHDon" (tính chất hoá đơn). Unverified against a
    /// real sample (see the parsing note above) — passes non-numeric text through unchanged so an
    /// unexpected real-world value is still visible to the user rather than silently dropped.
    /// </summary>
    private static string? NormalizeInvoiceNature(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return null;

        return rawValue.Trim() switch
        {
            "1" => "Hóa đơn gốc",
            "2" => "Hóa đơn thay thế",
            "3" => "Hóa đơn điều chỉnh",
            "4" => "Hóa đơn bị thay thế",
            "5" => "Hóa đơn bị điều chỉnh",
            "6" => "Hóa đơn bị hủy",
            var other => other
        };
    }

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
    /// If the tag is present but its content isn't a valid decimal, this is treated the same as
    /// the tag being absent: no field is added, rather than fabricating a zero value at full
    /// confidence. See <see cref="TryParseInvariantMoney"/> for the parsing rationale.
    /// </summary>
    private static void AddMoneyField(
        List<ExtractedField> fields, FieldName fieldName, string? rawValue, string providerFieldName)
    {
        if (!TryParseInvariantMoney(rawValue, out var value))
            return;

        fields.Add(new ExtractedField
        {
            FieldName = fieldName.ToString(),
            RawValue = rawValue,
            NormalizedValue = value.ToString("0.####################", CultureInfo.InvariantCulture),
            Confidence = 1.0,
            SourceType = ProviderFieldSource,
            ExtractionMethod = ProviderFieldSource,
            ProviderFieldName = providerFieldName
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
