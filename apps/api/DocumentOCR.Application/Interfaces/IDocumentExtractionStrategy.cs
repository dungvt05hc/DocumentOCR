using DocumentOCR.Application.Models;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// One candidate way of turning an uploaded document into structured fields (XML parsing,
/// PDF-text-layer + LLM, or OCR). <c>DocumentProcessingService</c> resolves all registered
/// strategies, tries them in <see cref="Priority"/> order (lower first), and uses the first one
/// whose <see cref="CanHandleAsync"/> returns true AND whose <see cref="ExtractAsync"/> succeeds —
/// a strategy that claims a document via <see cref="CanHandleAsync"/> but fails at
/// <see cref="ExtractAsync"/> (e.g. an LLM timeout) is treated the same as "can't handle it",
/// falling through to the next candidate rather than failing the document immediately.
/// </summary>
public interface IDocumentExtractionStrategy
{
    /// <summary>Human-readable identifier for logging/audit (e.g. "XmlInvoiceStrategy").</summary>
    string Name { get; }

    /// <summary>Lower values are tried first.</summary>
    int Priority { get; }

    /// <summary>Cheap, side-effect-free check of whether this strategy is applicable to <paramref name="input"/>.</summary>
    Task<bool> CanHandleAsync(DocumentInput input, CancellationToken ct = default);

    /// <summary>Does the actual extraction work. Must not throw for expected failure modes (parse error, provider timeout) — return <c>Success = false</c> instead.</summary>
    Task<StructuredExtractionResult> ExtractAsync(DocumentInput input, CancellationToken ct = default);
}
