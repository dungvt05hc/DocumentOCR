namespace DocumentOCR.Domain.Enums;

/// <summary>
/// Presentation/input-shape hint for a <c>ReviewField</c> in the dynamic review response —
/// tells the frontend which control to render, independent of the underlying storage type
/// (everything is still stored as string RawValue/NormalizedValue on <c>ExtractedField</c>).
/// </summary>
public enum ReviewFieldDataType
{
    Text = 0,
    Number = 1,
    Money = 2,
    Date = 3,
    Percentage = 4,
    Email = 5,
    Phone = 6,
    Url = 7,
    TaxCode = 8,
    Currency = 9,
    Enum = 10,
    MultilineText = 11
}
