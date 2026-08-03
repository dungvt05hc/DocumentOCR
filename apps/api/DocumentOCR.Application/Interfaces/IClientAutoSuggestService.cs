namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Best-effort post-processing step that assigns a <c>ClientProfile</c> to a freshly processed
/// document when its extracted seller tax code matches an active client on file. Runs after the
/// OCR/extraction pipeline has already saved fields — it never re-runs or alters extraction,
/// normalization, or validation. See docs/decisions.md for why this only covers the
/// "client is the seller" case today.
/// </summary>
public interface IClientAutoSuggestService
{
    /// <summary>
    /// Attempts to assign a matching <c>ClientProfile</c> to the document. No-op if the document
    /// already has a client assigned (manual or previous auto-suggest), doesn't exist, has no
    /// extracted supplier tax code, or no active client's tax code matches.
    /// </summary>
    /// <returns>True if a client was assigned by this call.</returns>
    Task<bool> TrySuggestAndAssignAsync(Guid documentId, CancellationToken ct = default);
}
