using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Best-effort post-processing steps that run after a document has been processed and its fields
/// saved — never re-run or alter extraction, normalization, or validation themselves.
/// </summary>
public interface IClientAutoSuggestService
{
    /// <summary>
    /// Attempts to assign a matching <c>ClientProfile</c> to the document, by SupplierTaxCode
    /// (the client is the seller — hóa đơn đầu ra) first, then BuyerTaxCode (the client is the
    /// buyer — hóa đơn đầu vào). No-op if the document already has a client assigned (manual or
    /// previous auto-suggest), doesn't exist, has no extracted tax code on either side, or no
    /// active client's tax code matches either.
    /// </summary>
    /// <returns>True if a client was assigned by this call.</returns>
    Task<bool> TrySuggestAndAssignAsync(Guid documentId, CancellationToken ct = default);

    /// <summary>
    /// Infers <see cref="Domain.Entities.Document.Direction"/> by comparing the document's
    /// extracted BuyerTaxCode/SupplierTaxCode against its assigned <c>ClientProfile.TaxCode</c>
    /// (set by <see cref="TrySuggestAndAssignAsync"/> or manually) — Buyer match → Purchase,
    /// Supplier match → Sale, no client or no match on either side → Unknown (and a
    /// low-severity validation warning nudging the user to pick manually). Always persists the
    /// resolved value, even Unknown, so it reflects the current fields/client on every call.
    /// </summary>
    Task<DocumentDirection> InferDirectionAsync(Guid documentId, CancellationToken ct = default);
}
