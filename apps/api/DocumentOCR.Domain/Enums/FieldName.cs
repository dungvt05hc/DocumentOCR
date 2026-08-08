namespace DocumentOCR.Domain.Enums;

public enum FieldName
{
    SupplierName = 0,
    SupplierTaxCode = 1,
    InvoiceNumber = 2,
    InvoiceDate = 3,
    SubtotalAmount = 4,
    VatAmount = 5,
    TotalAmount = 6,
    Currency = 7,
    DocumentType = 8,
    Notes = 9,

    // ── Added for the Vietnamese-invoice model expansion — appended, not interleaved, so
    // existing persisted values (0-9) never shift meaning. ──────────────────────────────
    BuyerName = 10,
    BuyerTaxCode = 11,
    BuyerAddress = 12,
    SupplierAddress = 13,

    /// <summary>Mẫu số hoá đơn (TT78 "KHMSHDon").</summary>
    InvoiceForm = 14,

    /// <summary>Ký hiệu hoá đơn (TT78 "KHHDon").</summary>
    InvoiceSymbol = 15,

    /// <summary>Mã của cơ quan thuế (TT78 "MCCQT").</summary>
    TaxAuthorityCode = 16,

    /// <summary>Tính chất hoá đơn: gốc/thay thế/điều chỉnh/huỷ.</summary>
    InvoiceNature = 17
}
