using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Resolves a document's <see cref="DocumentCategory"/> and looks up its <see cref="DocumentProfile"/>
/// (sections/fields/required-ness) for the dynamic review response. Profiles are static in-code
/// data for this iteration — see <c>Infrastructure/Profiles/DocumentProfileCatalog</c>.
/// </summary>
public interface IDocumentProfileCatalog
{
    /// <summary>
    /// Resolves the review-facing <see cref="DocumentCategory"/> from the same detection signal
    /// <see cref="DocumentType"/> already uses (the raw value of the "DocumentType" pseudo-field,
    /// e.g. "VatInvoice" or the newer "AppReceiptScreenshot"), falling back to a mapping from
    /// <paramref name="fallbackDocumentType"/> (the already-persisted <see cref="Document.DocumentType"/>-equivalent)
    /// when the raw value doesn't parse directly against <see cref="DocumentCategory"/>.
    /// </summary>
    DocumentCategory ResolveCategory(string? detectedDocumentTypeFieldValue, DocumentType fallbackDocumentType);

    /// <summary>Returns the profile for the given category. Every <see cref="DocumentCategory"/> value resolves to a profile — never null.</summary>
    DocumentProfile GetProfile(DocumentCategory category);
}
