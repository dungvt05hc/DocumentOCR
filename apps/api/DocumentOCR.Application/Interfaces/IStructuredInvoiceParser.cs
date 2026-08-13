using DocumentOCR.Application.Models;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Parses a structured invoice file (e.g. a Vietnamese TT78 e-invoice XML) directly into
/// <see cref="Domain.Entities.ExtractedField"/>s, bypassing OCR and heuristic field extraction
/// entirely. Implementations must not leak the source structure/schema outside the Application
/// layer beyond what <see cref="StructuredExtractionResult"/> exposes.
/// </summary>
public interface IStructuredInvoiceParser
{
    /// <summary>Whether this parser can handle the given upload based on content type/file name.</summary>
    bool CanParse(string contentType, string fileName);

    /// <summary>Parses the file content into extracted fields. Never throws on missing optional data.</summary>
    Task<StructuredExtractionResult> ParseAsync(Stream content, CancellationToken ct = default);
}
