namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// Wire contract for the PaddleOCR HTTP service's analyze response. These types describe the
/// JSON returned by the *external* PaddleOCR service (Docker/Python — not part of this repo) and
/// must never leak outside <see cref="PaddleOcrProvider"/>; callers only ever see
/// <see cref="Application.Models.NormalizedOcrDocument"/>.
/// <para>
/// Expected shape (see LOCAL_DEVELOPMENT.md for the full contract and a local run guide):
/// <code>
/// {
///   "success": true,
///   "errorMessage": null,
///   "pageCount": 1,
///   "fullText": "...",
///   "averageConfidence": 0.93,
///   "pages": [{
///     "pageNumber": 1, "width": 800.0, "height": 1200.0, "unit": "pixel",
///     "lines": [{
///       "text": "...", "confidence": 0.95,
///       "boundingBox": [[10,20],[200,20],[200,50],[10,50]],
///       "words": [{ "text": "...", "confidence": 0.96, "boundingBox": [[...]] }]
///     }]
///   }]
/// }
/// </code>
/// <c>words</c> is optional — PaddleOCR's default det+rec pipeline is line-level; omit or send
/// an empty array when word-level segmentation isn't available.
/// </para>
/// </summary>
internal sealed class PaddleOcrResponse
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public int PageCount { get; set; }
    public string? FullText { get; set; }
    public double? AverageConfidence { get; set; }
    public List<PaddleOcrPage>? Pages { get; set; }
}

internal sealed class PaddleOcrPage
{
    public int PageNumber { get; set; } = 1;
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? Unit { get; set; }
    public List<PaddleOcrLine>? Lines { get; set; }
}

internal sealed class PaddleOcrLine
{
    public string Text { get; set; } = string.Empty;
    public double? Confidence { get; set; }

    /// <summary>Quadrilateral polygon as [[x,y], [x,y], [x,y], [x,y]], in that vertex order.</summary>
    public List<List<double>>? BoundingBox { get; set; }

    /// <summary>Optional word-level breakdown; null/empty when the service only supports line-level output.</summary>
    public List<PaddleOcrWord>? Words { get; set; }
}

internal sealed class PaddleOcrWord
{
    public string Text { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public List<List<double>>? BoundingBox { get; set; }
}
