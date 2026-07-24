namespace DocumentOCR.Domain.Enums;

/// <summary>
/// Drives the dynamic review profile (sections/fields/required-ness) shown to the user.
/// Resolved fresh from the same detection signal as <see cref="DocumentType"/> — never persisted,
/// so introducing new categories here never requires a schema migration. See
/// <c>IDocumentProfileCatalog.ResolveCategory</c>.
/// </summary>
public enum DocumentCategory
{
    Unknown = 0,
    VatInvoice = 1,
    SalesReceipt = 2,
    PosReceipt = 3,
    RestaurantBill = 4,
    AppReceiptScreenshot = 5,
    InternationalInvoice = 6,
    CommercialInvoice = 7
}
