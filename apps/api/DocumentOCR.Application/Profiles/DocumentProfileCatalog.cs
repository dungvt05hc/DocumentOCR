using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Profiles;

/// <summary>
/// Static, in-code catalog of the 5 document review profiles. No DB/config-driven profile
/// management yet — this is deliberately simple for the MVP (see docs/decisions.md).
/// </summary>
public class DocumentProfileCatalog : IDocumentProfileCatalog
{
    public DocumentCategory ResolveCategory(string? detectedDocumentTypeFieldValue, DocumentType fallbackDocumentType)
    {
        // The extractor already writes a string into the "DocumentType" pseudo-field (e.g.
        // "VatInvoice", or the newer "AppReceiptScreenshot"/"InternationalInvoice"/"CommercialInvoice"
        // that don't exist on the legacy DocumentType enum at all). Try that richer 8-value parse
        // first; only fall back to the legacy DocumentType → DocumentCategory table below when it
        // doesn't match (covers documents processed before this feature existed, or where the
        // heuristic only produced one of the original 7 DocumentType values).
        if (!string.IsNullOrWhiteSpace(detectedDocumentTypeFieldValue)
            && Enum.TryParse<DocumentCategory>(detectedDocumentTypeFieldValue, ignoreCase: true, out var category))
        {
            return category;
        }

        return fallbackDocumentType switch
        {
            DocumentType.VatInvoice => DocumentCategory.VatInvoice,
            // DocumentType.Invoice is documented as "generic invoice, used when a more specific
            // category can't be determined" (including the default when no DocumentType field is
            // present at all) — it maps to the Unknown category's strict fallback profile rather
            // than the full VatInvoice profile, reproducing today's exact default behavior: tax
            // code stays required (Invoice was never in the legacy TaxCodeOptionalCategories set),
            // and required-field warnings stay keyed by the legacy FieldName values.
            DocumentType.Invoice => DocumentCategory.Unknown,
            DocumentType.Receipt => DocumentCategory.PosReceipt,
            DocumentType.PosReceipt => DocumentCategory.PosReceipt,
            DocumentType.RestaurantBill => DocumentCategory.RestaurantBill,
            DocumentType.ExpenseDocument => DocumentCategory.Unknown,
            _ => DocumentCategory.Unknown
        };
    }

    public DocumentProfile GetProfile(DocumentCategory category) => category switch
    {
        DocumentCategory.VatInvoice => VatInvoiceProfile,
        DocumentCategory.PosReceipt or DocumentCategory.SalesReceipt => PosReceiptProfile,
        DocumentCategory.AppReceiptScreenshot => AppReceiptScreenshotProfile,
        DocumentCategory.RestaurantBill => RestaurantBillProfile,
        DocumentCategory.InternationalInvoice or DocumentCategory.CommercialInvoice => InternationalInvoiceProfile,
        _ => UnknownFallbackProfile
    };

    // ── Profile definitions ─────────────────────────────────────────────────────

    /// <summary>
    /// Vietnamese-invoice layout: 4 clusters matching how a printed hóa đơn GTGT is actually laid
    /// out — "seller"/"buyer" section keys and the "SellerName"/"SellerTaxCode" field keys (with
    /// their legacy SupplierName/SupplierTaxCode aliases) are unchanged from before this reshape,
    /// since existing warnings/exports/tests are keyed by them; every other field key here maps
    /// 1:1 to its new <see cref="FieldName"/> enum value. "VatRate" is deliberately not a field
    /// here — it now lives in the per-line <c>InvoiceTaxBreakdown</c> table (see
    /// <c>DocumentReviewResponse.TaxBreakdown</c>), rendered as its own editable table rather than
    /// a single flat field.
    /// </summary>
    private static readonly DocumentProfile VatInvoiceProfile = new()
    {
        Category = DocumentCategory.VatInvoice,
        DisplayName = "Vietnamese VAT invoice",
        Sections =
        [
            new ProfileSection
            {
                SectionKey = "invoice",
                Title = "Thông tin hoá đơn",
                DisplayOrder = 0,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.InvoiceForm), Label = "Mẫu số", DisplayOrder = 0 },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.InvoiceSymbol), Label = "Ký hiệu", DisplayOrder = 1 },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.InvoiceNumber), Label = "Số hoá đơn",
                        IsRequired = true, DisplayOrder = 2, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.InvoiceDate), Label = "Ngày lập", DataType = ReviewFieldDataType.Date,
                        IsRequired = true, DisplayOrder = 3, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.TaxAuthorityCode), Label = "Mã CQT", DisplayOrder = 4 },
                    new ProfileFieldDefinition { FieldKey = "LookupCode", Label = "Mã tra cứu", DisplayOrder = 5 },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.InvoiceNature), Label = "Tính chất", DisplayOrder = 6 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "seller",
                Title = "Người bán",
                DisplayOrder = 1,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        FieldKey = "SellerName", Label = "Tên người bán", DataType = ReviewFieldDataType.Text,
                        IsRequired = true, DisplayOrder = 0, MissingSeverity = ValidationSeverity.High,
                        AliasFieldNames = [nameof(FieldName.SupplierName)]
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = "SellerTaxCode", Label = "Mã số thuế", DataType = ReviewFieldDataType.TaxCode,
                        IsRequired = true, DisplayOrder = 1, MissingSeverity = ValidationSeverity.High,
                        AliasFieldNames = [nameof(FieldName.SupplierTaxCode)]
                    },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.SupplierAddress), Label = "Địa chỉ", DisplayOrder = 2 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "buyer",
                Title = "Người mua",
                DisplayOrder = 2,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.BuyerName), Label = "Tên người mua", DisplayOrder = 0 },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.BuyerTaxCode), Label = "Mã số thuế", DataType = ReviewFieldDataType.TaxCode, DisplayOrder = 1 },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.BuyerAddress), Label = "Địa chỉ", DisplayOrder = 2 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "amounts",
                Title = "Số tiền",
                DisplayOrder = 3,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.SubtotalAmount), Label = "Tiền hàng", DataType = ReviewFieldDataType.Money, DisplayOrder = 0 },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.VatAmount), Label = "Tổng thuế", DataType = ReviewFieldDataType.Money, DisplayOrder = 1 },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.TotalAmount), Label = "Tổng thanh toán", DataType = ReviewFieldDataType.Money,
                        IsRequired = true, DisplayOrder = 2, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition { FieldKey = "AmountInWords", Label = "Tiền bằng chữ", DisplayOrder = 3 },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Currency), Label = "Đồng tiền", DataType = ReviewFieldDataType.Currency, DisplayOrder = 4 },
                    new ProfileFieldDefinition { FieldKey = "PaymentMethod", Label = "Hình thức thanh toán", DisplayOrder = 5 },
                    // Not one of the 4 clusters' listed fields — kept here (its previous home)
                    // rather than dropped, so free-text notes already on a document, or entered
                    // by a user, are never silently discarded.
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Notes), Label = "Ghi chú", DataType = ReviewFieldDataType.MultilineText, DisplayOrder = 6 }
                ]
            }
        ]
    };

    private static readonly DocumentProfile PosReceiptProfile = new()
    {
        Category = DocumentCategory.PosReceipt,
        DisplayName = "POS / sales receipt",
        Sections =
        [
            new ProfileSection
            {
                SectionKey = "merchant",
                Title = "Merchant information",
                DisplayOrder = 0,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        FieldKey = "MerchantName", Label = "Merchant name",
                        IsRequired = true, DisplayOrder = 0, MissingSeverity = ValidationSeverity.High,
                        AliasFieldNames = [nameof(FieldName.SupplierName)]
                    },
                    new ProfileFieldDefinition { FieldKey = "MerchantPhone", Label = "Merchant phone", DataType = ReviewFieldDataType.Phone, DisplayOrder = 1 },
                    new ProfileFieldDefinition { FieldKey = "MerchantAddress", Label = "Merchant address", DisplayOrder = 2 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "receipt",
                Title = "Receipt information",
                DisplayOrder = 1,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        FieldKey = "ReceiptNumber", Label = "Receipt number", DisplayOrder = 0,
                        AliasFieldNames = [nameof(FieldName.InvoiceNumber)]
                    },
                    new ProfileFieldDefinition
                    {
                        // Required so a missing receipt date still surfaces a warning, but at
                        // Warning (not High) severity — receipts commonly omit/shorthand it.
                        FieldKey = "ReceiptDate", Label = "Receipt date", DataType = ReviewFieldDataType.Date,
                        IsRequired = true, DisplayOrder = 1, MissingSeverity = ValidationSeverity.Warning,
                        AliasFieldNames = [nameof(FieldName.InvoiceDate)]
                    },
                    new ProfileFieldDefinition { FieldKey = "Cashier", Label = "Cashier", DisplayOrder = 2 },
                    new ProfileFieldDefinition { FieldKey = "TableNumber", Label = "Table number", DisplayOrder = 3 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "amounts",
                Title = "Amounts",
                DisplayOrder = 2,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.SubtotalAmount), Label = "Subtotal", DataType = ReviewFieldDataType.Money, DisplayOrder = 0 },
                    new ProfileFieldDefinition { FieldKey = "DiscountAmount", Label = "Discount amount", DataType = ReviewFieldDataType.Money, DisplayOrder = 1 },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.TotalAmount), Label = "Total amount", DataType = ReviewFieldDataType.Money,
                        IsRequired = true, DisplayOrder = 2, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Currency), Label = "Currency", DataType = ReviewFieldDataType.Currency, DisplayOrder = 3 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "notes",
                Title = "Notes",
                DisplayOrder = 3,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = "PaymentMethod", Label = "Payment method", DisplayOrder = 0 },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Notes), Label = "Notes", DataType = ReviewFieldDataType.MultilineText, DisplayOrder = 1 }
                ]
            }
        ]
    };

    private static readonly DocumentProfile AppReceiptScreenshotProfile = new()
    {
        Category = DocumentCategory.AppReceiptScreenshot,
        DisplayName = "App receipt screenshot",
        Sections =
        [
            new ProfileSection
            {
                SectionKey = "store",
                Title = "Store information",
                DisplayOrder = 0,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        FieldKey = "StoreName", Label = "Store name",
                        IsRequired = true, DisplayOrder = 0, MissingSeverity = ValidationSeverity.High,
                        AliasFieldNames = [nameof(FieldName.SupplierName)]
                    },
                    new ProfileFieldDefinition { FieldKey = "StoreUrl", Label = "Store URL", DataType = ReviewFieldDataType.Url, DisplayOrder = 1 },
                    new ProfileFieldDefinition { FieldKey = "QrCodeValue", Label = "QR code value", DisplayOrder = 2 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "order",
                Title = "Order information",
                DisplayOrder = 1,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        FieldKey = "OrderCode", Label = "Order code",
                        IsRequired = true, DisplayOrder = 0, MissingSeverity = ValidationSeverity.High,
                        AliasFieldNames = [nameof(FieldName.InvoiceNumber)]
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = "OrderDate", Label = "Order date", DataType = ReviewFieldDataType.Date, DisplayOrder = 1,
                        AliasFieldNames = [nameof(FieldName.InvoiceDate)]
                    },
                    new ProfileFieldDefinition { FieldKey = "CustomerName", Label = "Customer name", DisplayOrder = 2 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "amounts",
                Title = "Amounts",
                DisplayOrder = 2,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        // "TotalAmount if visible" — required, but only Warning severity since
                        // screenshots frequently crop the total out of frame.
                        FieldKey = nameof(FieldName.TotalAmount), Label = "Total amount", DataType = ReviewFieldDataType.Money,
                        IsRequired = true, DisplayOrder = 0, MissingSeverity = ValidationSeverity.Warning
                    },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Currency), Label = "Currency", DataType = ReviewFieldDataType.Currency, DisplayOrder = 1 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "notes",
                Title = "Notes",
                DisplayOrder = 3,
                Fields = [new ProfileFieldDefinition { FieldKey = nameof(FieldName.Notes), Label = "Notes", DataType = ReviewFieldDataType.MultilineText, DisplayOrder = 0 }]
            }
        ]
    };

    private static readonly DocumentProfile RestaurantBillProfile = new()
    {
        Category = DocumentCategory.RestaurantBill,
        DisplayName = "Restaurant bill",
        IsExperimental = true,
        Sections =
        [
            new ProfileSection
            {
                SectionKey = "merchant",
                Title = "Merchant information",
                DisplayOrder = 0,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = "MerchantName", Label = "Merchant name", DisplayOrder = 0, AliasFieldNames = [nameof(FieldName.SupplierName)] },
                    new ProfileFieldDefinition { FieldKey = "MerchantPhone", Label = "Merchant phone", DataType = ReviewFieldDataType.Phone, DisplayOrder = 1 },
                    new ProfileFieldDefinition { FieldKey = "MerchantAddress", Label = "Merchant address", DisplayOrder = 2 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "bill",
                Title = "Bill information",
                DisplayOrder = 1,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = "BillNumber", Label = "Bill number", DisplayOrder = 0, AliasFieldNames = [nameof(FieldName.InvoiceNumber)] },
                    new ProfileFieldDefinition { FieldKey = "BillDate", Label = "Bill date", DataType = ReviewFieldDataType.Date, DisplayOrder = 1, AliasFieldNames = [nameof(FieldName.InvoiceDate)] },
                    new ProfileFieldDefinition { FieldKey = "TableNumber", Label = "Table number", DisplayOrder = 2 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "amounts",
                Title = "Amounts",
                DisplayOrder = 2,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        // "Required if readable" — required, but Warning severity given the
                        // inherently lower reliability of handwritten/mixed bills.
                        FieldKey = nameof(FieldName.TotalAmount), Label = "Total amount", DataType = ReviewFieldDataType.Money,
                        IsRequired = true, DisplayOrder = 0, MissingSeverity = ValidationSeverity.Warning
                    },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Currency), Label = "Currency", DataType = ReviewFieldDataType.Currency, DisplayOrder = 1 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "notes",
                Title = "Notes",
                DisplayOrder = 3,
                Fields = [new ProfileFieldDefinition { FieldKey = nameof(FieldName.Notes), Label = "Notes", DataType = ReviewFieldDataType.MultilineText, DisplayOrder = 0 }]
            }
        ]
    };

    private static readonly DocumentProfile InternationalInvoiceProfile = new()
    {
        Category = DocumentCategory.InternationalInvoice,
        DisplayName = "International / commercial invoice",
        Sections =
        [
            new ProfileSection
            {
                SectionKey = "vendor",
                Title = "Vendor information",
                DisplayOrder = 0,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        FieldKey = "VendorName", Label = "Vendor name",
                        IsRequired = true, DisplayOrder = 0, MissingSeverity = ValidationSeverity.High,
                        AliasFieldNames = [nameof(FieldName.SupplierName)]
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = "VendorTaxCode", Label = "Vendor tax code", DataType = ReviewFieldDataType.TaxCode,
                        DisplayOrder = 1, AliasFieldNames = [nameof(FieldName.SupplierTaxCode)]
                    },
                    new ProfileFieldDefinition { FieldKey = "VendorAddress", Label = "Vendor address", DisplayOrder = 2 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "buyer",
                Title = "Buyer information",
                DisplayOrder = 1,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = "BuyerName", Label = "Buyer name", DisplayOrder = 0 },
                    new ProfileFieldDefinition { FieldKey = "BuyerTaxCode", Label = "Buyer tax code", DataType = ReviewFieldDataType.TaxCode, DisplayOrder = 1 },
                    new ProfileFieldDefinition { FieldKey = "BuyerAddress", Label = "Buyer address", DisplayOrder = 2 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "invoice",
                Title = "Invoice information",
                DisplayOrder = 2,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.InvoiceNumber), Label = "Invoice number",
                        IsRequired = true, DisplayOrder = 0, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.InvoiceDate), Label = "Invoice date", DataType = ReviewFieldDataType.Date,
                        IsRequired = true, DisplayOrder = 1, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition { FieldKey = "DueDate", Label = "Due date", DataType = ReviewFieldDataType.Date, DisplayOrder = 2 },
                    new ProfileFieldDefinition { FieldKey = "PONumber", Label = "PO number", DisplayOrder = 3 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "amounts",
                Title = "Amounts",
                DisplayOrder = 3,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.SubtotalAmount), Label = "Subtotal", DataType = ReviewFieldDataType.Money, DisplayOrder = 0 },
                    new ProfileFieldDefinition { FieldKey = "DiscountAmount", Label = "Discount amount", DataType = ReviewFieldDataType.Money, DisplayOrder = 1 },
                    new ProfileFieldDefinition
                    {
                        FieldKey = "TaxAmount", Label = "Tax amount", DataType = ReviewFieldDataType.Money,
                        DisplayOrder = 2, AliasFieldNames = [nameof(FieldName.VatAmount)]
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.TotalAmount), Label = "Total amount", DataType = ReviewFieldDataType.Money,
                        IsRequired = true, DisplayOrder = 3, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Currency), Label = "Currency", DataType = ReviewFieldDataType.Currency, DisplayOrder = 4 }
                ]
            },
            new ProfileSection
            {
                SectionKey = "notes",
                Title = "Notes",
                DisplayOrder = 4,
                Fields =
                [
                    new ProfileFieldDefinition { FieldKey = "PaymentTerms", Label = "Payment terms", DisplayOrder = 0 },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Notes), Label = "Notes", DataType = ReviewFieldDataType.MultilineText, DisplayOrder = 1 }
                ]
            }
        ]
    };

    /// <summary>
    /// Fallback used for <see cref="DocumentCategory.Unknown"/> (including today's behavior when
    /// no "DocumentType" field was detected at all). Deliberately reproduces the exact rule set
    /// <c>FieldValidationService</c> applied before this refactor — SupplierName/SupplierTaxCode/
    /// InvoiceNumber/InvoiceDate/TotalAmount all required at High severity — so untyped/legacy
    /// documents don't silently become more lenient.
    /// </summary>
    private static readonly DocumentProfile UnknownFallbackProfile = new()
    {
        Category = DocumentCategory.Unknown,
        DisplayName = "Unclassified document",
        Sections =
        [
            new ProfileSection
            {
                SectionKey = "general",
                Title = "Document information",
                DisplayOrder = 0,
                Fields =
                [
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.SupplierName), Label = "Supplier name",
                        IsRequired = true, DisplayOrder = 0, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.SupplierTaxCode), Label = "Supplier tax code", DataType = ReviewFieldDataType.TaxCode,
                        IsRequired = true, DisplayOrder = 1, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.InvoiceNumber), Label = "Invoice number",
                        IsRequired = true, DisplayOrder = 2, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.InvoiceDate), Label = "Invoice date", DataType = ReviewFieldDataType.Date,
                        IsRequired = true, DisplayOrder = 3, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition
                    {
                        FieldKey = nameof(FieldName.TotalAmount), Label = "Total amount", DataType = ReviewFieldDataType.Money,
                        IsRequired = true, DisplayOrder = 4, MissingSeverity = ValidationSeverity.High
                    },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Currency), Label = "Currency", DataType = ReviewFieldDataType.Currency, DisplayOrder = 5 },
                    new ProfileFieldDefinition { FieldKey = nameof(FieldName.Notes), Label = "Notes", DataType = ReviewFieldDataType.MultilineText, DisplayOrder = 6 }
                ]
            }
        ]
    };
}
