using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Maps structured fields returned by the OCR provider to domain ExtractedField entities.
/// Azure Document Intelligence returns keys like "VendorName", "InvoiceId", etc.
/// This service normalizes those keys to our canonical field name strings.
/// </summary>
public class FieldExtractionService : IFieldExtractionService
{
    // Maps Azure Document Intelligence field keys → canonical field name strings
    private static readonly Dictionary<string, string> FieldKeyMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Azure prebuilt-invoice keys
            { "VendorName",       nameof(FieldName.SupplierName) },
            { "VendorTaxId",      nameof(FieldName.SupplierTaxCode) },
            { "InvoiceId",        nameof(FieldName.InvoiceNumber) },
            { "InvoiceDate",      nameof(FieldName.InvoiceDate) },
            { "SubTotal",         nameof(FieldName.SubtotalAmount) },
            { "TotalTax",         nameof(FieldName.VatAmount) },
            { "InvoiceTotal",     nameof(FieldName.TotalAmount) },
            { "CurrencyCode",     nameof(FieldName.Currency) },

            // Alternative keys from other providers / custom models
            { "SupplierName",     nameof(FieldName.SupplierName) },
            { "SupplierTaxCode",  nameof(FieldName.SupplierTaxCode) },
            { "InvoiceNumber",    nameof(FieldName.InvoiceNumber) },
            { "SubtotalAmount",   nameof(FieldName.SubtotalAmount) },
            { "VatAmount",        nameof(FieldName.VatAmount) },
            { "TotalAmount",      nameof(FieldName.TotalAmount) },
            { "Notes",            nameof(FieldName.Notes) },
            { "DocumentType",     nameof(FieldName.DocumentType) }
        };

    public IReadOnlyList<ExtractedField> Extract(Guid documentId, OcrResult ocrResult)
    {
        var result = new Dictionary<string, ExtractedField>();

        foreach (var ocrField in ocrResult.Fields)
        {
            if (!FieldKeyMap.TryGetValue(ocrField.FieldKey, out var fieldName)) continue;

            // Last-write wins if the same field name appears multiple times
            result[fieldName] = new ExtractedField
            {
                DocumentId = documentId,
                FieldName = fieldName,
                RawValue = ocrField.Value,
                Confidence = ocrField.Confidence,
                PageNumber = ocrField.PageNumber
            };
        }

        return result.Values.ToList();
    }
}
