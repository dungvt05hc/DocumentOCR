namespace DocumentOCR.Application.DTOs;

public class UpdateFieldsRequest
{
    public List<FieldUpdateItem> Fields { get; set; } = new();
}

public class FieldUpdateItem
{
    public string FieldName { get; set; } = string.Empty;

    /// <summary>The corrected value entered by the user.</summary>
    public string? NormalizedValue { get; set; }
}
