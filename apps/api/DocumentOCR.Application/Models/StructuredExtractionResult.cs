using DocumentOCR.Domain.Entities;

namespace DocumentOCR.Application.Models;

/// <summary>
/// Result of one <see cref="Interfaces.IDocumentExtractionStrategy"/> attempt — the single shape
/// all three strategies (XML, PDF-text-layer+LLM, OCR) return, so <c>DocumentProcessingService</c>
/// has one persistence path regardless of which strategy actually served a document.
/// </summary>
public sealed record StructuredExtractionResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Fields parsed/extracted. <see cref="ExtractedField.DocumentId"/> is not set here — the caller assigns it.</summary>
    public IReadOnlyList<ExtractedField> Fields { get; init; } = [];

    /// <summary>VAT-rate breakdown lines. <see cref="InvoiceTaxBreakdown.DocumentId"/> is not set here.</summary>
    public IReadOnlyList<InvoiceTaxBreakdown> TaxBreakdown { get; init; } = [];

    /// <summary>The original textual source content (raw XML, or PDF text-layer text), kept for artifact/audit persistence.</summary>
    public string? RawSourceText { get; init; }

    /// <summary>Invoice template code (e.g. TT78 "KHMSHDon"), if present.</summary>
    public string? InvoiceTemplateCode { get; init; }

    /// <summary>Invoice serial (e.g. TT78 "KHHDon"), if present.</summary>
    public string? InvoiceSerial { get; init; }

    // ── OcrProviderLog / Document persistence metadata ──────────────────────────
    // Every strategy populates these so DocumentProcessingService can write one OcrProviderLog
    // row and update Document.PageCount/TablesJson the same way regardless of source.

    /// <summary>Provider/strategy identifier for the audit trail (e.g. "TT78Xml", "PdfTextLayer", "AzureDocumentIntelligence").</summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>Provider-specific model identifier, when applicable (e.g. "prebuilt-layout", "gemini-2.5-flash").</summary>
    public string? ModelId { get; init; }

    public int PageCount { get; init; }
    public double ProcessingTimeMs { get; init; }
    public decimal EstimatedCost { get; init; }

    /// <summary>Per-page structured data, for persisting <see cref="Domain.Entities.DocumentPage"/> rows. Empty when the source has no page concept (e.g. XML).</summary>
    public IReadOnlyList<OcrPage> Pages { get; init; } = [];

    /// <summary>Detected tables, for <see cref="Domain.Entities.Document.TablesJson"/>. Null when the strategy doesn't detect tables.</summary>
    public IReadOnlyList<OcrTable>? Tables { get; init; }

    public string? RawResponseJson { get; init; }
    public string? RawResponsePath { get; init; }
    public string? NormalizedResultPath { get; init; }

    /// <summary>
    /// Count of candidate values a strategy discarded because they failed verification (e.g.
    /// <c>PdfTextLayerLlmStrategy</c>'s anti-hallucination sourceText check) — distinct from a
    /// field simply not being found. Always 0 for strategies with no such verification step (XML,
    /// OCR). Not persisted anywhere in the production pipeline; exists for benchmark/audit reporting.
    /// </summary>
    public int RejectedFieldCount { get; init; }
}
