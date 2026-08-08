namespace DocumentOCR.Domain.Enums;

/// <summary>
/// Whether a document is a purchase (hóa đơn đầu vào — the org's client is the buyer) or a sale
/// (hóa đơn đầu ra — the client is the seller), inferred by matching extracted tax codes against
/// the assigned <c>ClientProfile.TaxCode</c>. See <c>IClientAutoSuggestService.InferDirectionAsync</c>.
/// </summary>
public enum DocumentDirection
{
    Unknown = 0,
    Purchase = 1,
    Sale = 2
}
