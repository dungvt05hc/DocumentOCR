namespace DocumentOCR.Application.DTOs;

public class UpdateFieldsRequest
{
    public List<FieldUpdateItem> Fields { get; set; } = new();

    /// <summary>Edited table cells, addressed by the same TableId/RowIndex/ColumnKey the review response uses. Patched into the document's stored tables.</summary>
    public List<TableUpdateItem> Tables { get; set; } = new();

    /// <summary>
    /// Edited line item candidates. Accepted for API-contract completeness but intentionally
    /// not persisted for MVP — line items have no backing entity and are always re-derived from
    /// the document's stored tables. See docs/status.md.
    /// </summary>
    public List<LineItemUpdateItem> LineItems { get; set; } = new();
}

public class FieldUpdateItem
{
    public string FieldName { get; set; } = string.Empty;

    /// <summary>The corrected value entered by the user.</summary>
    public string? NormalizedValue { get; set; }

    /// <summary>Optional — only sent when the client also wants to override RawValue (e.g. saving a brand-new profile-only field with no prior OCR value).</summary>
    public string? RawValue { get; set; }
}

public class TableUpdateItem
{
    public string TableId { get; set; } = string.Empty;
    public List<TableRowUpdateItem> Rows { get; set; } = new();
}

public class TableRowUpdateItem
{
    public int RowIndex { get; set; }
    public List<TableCellUpdateItem> Cells { get; set; } = new();
}

public class TableCellUpdateItem
{
    public string? ColumnKey { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? NormalizedValue { get; set; }
}

public class LineItemUpdateItem
{
    public int LineNumber { get; set; }
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
}
