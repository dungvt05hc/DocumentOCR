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
        var totals = FindDescendant(dataRoot, "TToan");

        var invoiceNumber = GetChildValue(generalInfo, "SHDon");
        var templateCode = GetChildValue(generalInfo, "KHMSHDon");
        var serial = GetChildValue(generalInfo, "KHHDon");
        var invoiceDate = GetChildValue(generalInfo, "NLap");
        var currency = GetChildValue(generalInfo, "DVTTe");

        var supplierTaxCode = GetChildValue(seller, "MST");
        var supplierName = GetChildValue(seller, "Ten");

        var subtotal = GetChildValue(totals, "TgTCThue");
        var vat = GetChildValue(totals, "TgTThue");
        var total = GetChildValue(totals, "TgTTTBSo");

        var fields = new List<ExtractedField>();

        AddField(fields, FieldName.InvoiceNumber, invoiceNumber, "SHDon");
        AddField(fields, FieldName.InvoiceDate, invoiceDate, "NLap");
        AddField(fields, FieldName.SupplierTaxCode, supplierTaxCode, "MST");
        AddField(fields, FieldName.SupplierName, supplierName, "Ten");
        AddField(fields, FieldName.SubtotalAmount, subtotal, "TgTCThue");
        AddField(fields, FieldName.VatAmount, vat, "TgTThue");
        AddField(fields, FieldName.TotalAmount, total, "TgTTTBSo");

        if (!string.IsNullOrWhiteSpace(currency))
            AddField(fields, FieldName.Currency, currency, "DVTTe");
        else
            AddField(fields, FieldName.Currency, "VND", providerFieldName: null);

        var invoiceSerialLabel = BuildInvoiceSerialLabel(templateCode, serial);
        if (invoiceSerialLabel is not null)
            AddField(fields, FieldName.Notes, invoiceSerialLabel, providerFieldName: null);

        // TT78 XML is definitionally a formal VAT invoice — synthesized, not read from a tag, so
        // downstream document-type detection (DocumentProcessingService.GetDetectedDocumentType)
        // resolves the correct review profile instead of falling back to Unknown.
        AddField(fields, FieldName.DocumentType, nameof(DocumentType.VatInvoice), providerFieldName: null);

        return new StructuredInvoiceResult
        {
            Success = true,
            Fields = fields,
            RawXml = rawXml,
            InvoiceTemplateCode = templateCode,
            InvoiceSerial = serial
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
        List<ExtractedField> fields, FieldName fieldName, string? rawValue, string? providerFieldName)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return;

        fields.Add(new ExtractedField
        {
            FieldName = fieldName.ToString(),
            RawValue = rawValue,
            Confidence = 1.0,
            SourceType = ProviderFieldSource,
            ExtractionMethod = ProviderFieldSource,
            ProviderFieldName = providerFieldName
        });
    }

    private static string? BuildInvoiceSerialLabel(string? templateCode, string? serial)
    {
        if (templateCode is null && serial is null)
            return null;

        var parts = new List<string>();
        if (templateCode is not null)
            parts.Add($"Mẫu số {templateCode}");
        if (serial is not null)
            parts.Add($"Ký hiệu {serial}");

        return string.Join(" - ", parts);
    }
}
