using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.OcrBenchmark;

/// <summary>
/// Converts a TT78 XML e-invoice's parsed fields (<c>TT78XmlInvoiceParser</c>, full-confidence,
/// read directly from the schema) into a <see cref="GroundTruthRow"/> — ground truth derived from
/// the invoice's own structured source instead of a manually-authored CSV row, for the
/// XML/PDF-pair comparison pass in Program.cs.
/// </summary>
public static class XmlGroundTruthBuilder
{
    public static GroundTruthRow FromParsedXmlFields(string pdfFileName, IReadOnlyList<ExtractedField> fields) => new(
        FileName: pdfFileName,
        DocumentCategory: FieldValue(fields, FieldName.DocumentType),
        DocumentSubType: null,
        ExpectedSupplierName: FieldValue(fields, FieldName.SupplierName),
        ExpectedSupplierTaxCode: FieldValue(fields, FieldName.SupplierTaxCode),
        ExpectedBuyerName: FieldValue(fields, FieldName.BuyerName),
        ExpectedBuyerTaxCode: FieldValue(fields, FieldName.BuyerTaxCode),
        ExpectedInvoiceNumber: FieldValue(fields, FieldName.InvoiceNumber),
        ExpectedInvoiceDate: FieldValue(fields, FieldName.InvoiceDate),
        ExpectedSubtotalAmount: FieldValue(fields, FieldName.SubtotalAmount),
        ExpectedVatAmount: FieldValue(fields, FieldName.VatAmount),
        ExpectedTotalAmount: FieldValue(fields, FieldName.TotalAmount),
        ExpectedCurrency: FieldValue(fields, FieldName.Currency),
        QualityLevel: null,
        Notes: "Derived from paired XML source, not manually authored.");

    private static string? FieldValue(IReadOnlyList<ExtractedField> fields, FieldName fieldName)
    {
        var field = fields.FirstOrDefault(f => f.FieldName == fieldName.ToString());
        if (field is null) return null;
        return !string.IsNullOrWhiteSpace(field.NormalizedValue) ? field.NormalizedValue : field.RawValue;
    }
}
