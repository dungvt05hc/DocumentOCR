using DocumentOCR.Application.Models;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Text-only LLM field extraction for a Vietnamese VAT-invoice PDF text layer. Implementations
/// live in Infrastructure (provider SDK/HTTP details never leak here — same "port" pattern as
/// <see cref="IDocumentOcrProvider"/>) and must use the provider's native structured-output
/// mechanism (schema/tool-use), never a "reply with JSON" prompt parsed by hand.
/// <para>
/// Throws on failure (HTTP error, timeout, malformed response) — the caller
/// (<c>PdfTextLayerLlmStrategy</c>) is responsible for catching and falling through to OCR, per
/// <see cref="IDocumentExtractionStrategy"/>'s contract.
/// </para>
/// </summary>
public interface ILlmExtractionClient
{
    Task<LlmExtractionResult> ExtractAsync(string documentText, CancellationToken ct = default);
}
