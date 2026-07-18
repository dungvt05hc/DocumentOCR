namespace DocumentOCR.Application.Models;

/// <summary>A single cell within an <see cref="OcrTableResult"/>.</summary>
public sealed class OcrTableCellResult
{
    public int RowIndex { get; init; }
    public int ColumnIndex { get; init; }
    public string Content { get; init; } = string.Empty;

    /// <summary>Provider cell kind (e.g. "columnHeader", "rowHeader", "content"), when available.</summary>
    public string? Kind { get; init; }

    public BoundingBox? BoundingBox { get; init; }
}

/// <summary>A table detected by the OCR provider (e.g. Azure Document Intelligence layout tables).</summary>
public sealed class OcrTableResult
{
    /// <summary>1-based page number where the table starts, when available.</summary>
    public int? PageNumber { get; init; }

    public int RowCount { get; init; }
    public int ColumnCount { get; init; }
    public IReadOnlyList<OcrTableCellResult> Cells { get; init; } = [];
}
