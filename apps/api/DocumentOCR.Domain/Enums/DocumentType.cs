namespace DocumentOCR.Domain.Enums;

public enum DocumentType
{
    Unknown = 0,

    /// <summary>Generic invoice, used when a more specific category can't be determined.</summary>
    Invoice = 1,

    /// <summary>Generic receipt, used when a more specific category can't be determined.</summary>
    Receipt = 2,

    ExpenseDocument = 3,

    /// <summary>A formal Vietnamese VAT invoice ("hóa đơn giá trị gia tăng") — requires SupplierTaxCode.</summary>
    VatInvoice = 4,

    /// <summary>A POS/sales receipt ("hóa đơn bán hàng") — SupplierTaxCode is optional.</summary>
    PosReceipt = 5,

    /// <summary>A restaurant/food-service bill — SupplierTaxCode is optional.</summary>
    RestaurantBill = 6
}
