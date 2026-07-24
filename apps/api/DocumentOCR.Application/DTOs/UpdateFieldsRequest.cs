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

    /// <summary>Optional — only sent when the client also wants to override RawValue (e.g. saving a brand-new profile-only field with no prior OCR value).</summary>
    public string? RawValue { get; set; }
}
