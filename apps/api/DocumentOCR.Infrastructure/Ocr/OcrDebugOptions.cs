namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>Gates the review response's light debug summary and the <c>GET /api/documents/{id}/ocr-debug</c> endpoint.</summary>
public sealed class OcrDebugOptions
{
    public const string SectionName = "OcrDebug";

    /// <summary>When false, <c>DocumentReviewResponse.DebugData</c> stays null and the ocr-debug endpoint returns 404.</summary>
    public bool Enabled { get; set; }

    /// <summary>When true (and <see cref="Enabled"/>), the ocr-debug endpoint may include the raw provider response JSON content, not just its storage path. Never enable in production.</summary>
    public bool ExposeRawJson { get; set; }
}
