namespace DocumentOCR.Application.DTOs;

/// <summary>
/// A table detected by the OCR provider's layout analysis (see <c>Models.OcrTable</c>), reshaped
/// for review/export: cells arranged into a header row plus data rows, with a best-effort
/// canonical <see cref="ReviewTableColumn.NormalizedKey"/> per column (Description/Quantity/
/// UnitPrice/Amount) so the frontend and Excel export don't need to re-derive it.
/// </summary>
public class ReviewTable
{
    public string TableId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public int? PageNumber { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }

    /// <summary>Document-level table confidence, when the provider exposes one. Null for MVP — Azure's layout table mapping does not carry a per-table score today.</summary>
    public double? Confidence { get; set; }

    /// <summary>Reserved for a future distinction (e.g. "LineItems" vs "Summary") — always null for now.</summary>
    public string? TableType { get; set; }

    public List<ReviewTableColumn> Columns { get; set; } = new();
    public List<ReviewTableRow> Rows { get; set; } = new();
    public string? SourceBoundingBoxJson { get; set; }
}

public class ReviewTableColumn
{
    public int ColumnIndex { get; set; }
    public string ColumnKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>Canonical key when the header label matches a known vocabulary (Description/Quantity/UnitPrice/Amount); null otherwise.</summary>
    public string? NormalizedKey { get; set; }
    public string? DataType { get; set; }
    public double? Confidence { get; set; }
}

public class ReviewTableRow
{
    public int RowIndex { get; set; }

    /// <summary>"Header" or "Data" — irregular/ragged rows are still included, just with fewer cells.</summary>
    public string RowType { get; set; } = "Data";
    public List<ReviewTableCell> Cells { get; set; } = new();
}

public class ReviewTableCell
{
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public string? ColumnKey { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? NormalizedValue { get; set; }

    /// <summary>Null for MVP — the underlying <c>OcrTableCell</c> mapping carries no per-cell confidence today.</summary>
    public double? Confidence { get; set; }
    public string? SourceBoundingBoxJson { get; set; }
    public bool IsHeader { get; set; }
    public bool IsEditable { get; set; } = true;
}

/// <summary>
/// A simple candidate line item built from a <see cref="ReviewTable"/> that has a Description
/// column plus at least one of Quantity/UnitPrice/Amount. Not persisted — always re-derived from
/// the document's stored tables. This is a basic MVP candidate builder, not full line-item
/// extraction: unparsable numeric cells are left null rather than failing the row.
/// </summary>
public class ReviewLineItem
{
    public int LineNumber { get; set; }
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }

    /// <summary>Synthetic extraction-confidence heuristic (not an OCR score) — lower when a numeric cell failed to parse or the column match was fuzzy. Used to flag a row "candidate/experimental" in the UI.</summary>
    public double? Confidence { get; set; }
    public string SourceTableId { get; set; } = string.Empty;
    public int SourceRowIndex { get; set; }
    public bool IsEditable { get; set; } = true;
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Development/debug view of the underlying OCR output for a document. Only populated when
/// <c>OcrDebug:Enabled</c> is on; never shown by default in the review UI. See
/// <c>GET /api/documents/{id}/ocr-debug</c> for the fuller version (lines/paragraphs/key-value
/// pairs are only available there, reloaded from the optional normalized-result blob).
/// </summary>
public class OcrDebugData
{
    public string? FullText { get; set; }
    public List<string> Lines { get; set; } = new();
    public List<string> Paragraphs { get; set; } = new();
    public List<OcrDebugKeyValuePair> KeyValuePairs { get; set; } = new();

    /// <summary>Storage path only — never the raw content — unless explicitly requested via <c>OcrDebug:ExposeRawJson</c>.</summary>
    public string? RawProviderResponsePath { get; set; }
    public string? NormalizedOcrResultPath { get; set; }
    public string? OcrSummary { get; set; }
}

public class OcrDebugKeyValuePair
{
    public string? Key { get; set; }
    public string? Value { get; set; }
    public double? Confidence { get; set; }
}

/// <summary>
/// Response for <c>GET /api/documents/{id}/ocr-debug</c> — a superset of <see cref="OcrDebugData"/>
/// that also includes tables, extracted fields, and warnings. Lines/Paragraphs/KeyValuePairs are
/// only populated when the document's normalized OCR result blob is available
/// (<c>Ocr:StoreNormalizedOcrResult</c>); otherwise they're empty rather than causing a failure.
/// </summary>
public class OcrDebugResponse
{
    public Guid DocumentId { get; set; }
    public string? FullText { get; set; }
    public List<string> Lines { get; set; } = new();
    public List<string> Paragraphs { get; set; } = new();
    public List<OcrDebugKeyValuePair> KeyValuePairs { get; set; } = new();
    public List<ReviewTable> Tables { get; set; } = new();
    public List<ExtractedFieldDto> Fields { get; set; } = new();
    public List<ValidationWarningDto> Warnings { get; set; } = new();

    /// <summary>Path only, never content, unless <c>OcrDebug:ExposeRawJson</c> is on (see <see cref="RawProviderResponseJson"/>).</summary>
    public string? RawProviderResponsePath { get; set; }
    public string? NormalizedOcrResultPath { get; set; }

    /// <summary>Only populated when <c>OcrDebug:ExposeRawJson</c> is true.</summary>
    public string? RawProviderResponseJson { get; set; }
    public string? OcrSummary { get; set; }
}
