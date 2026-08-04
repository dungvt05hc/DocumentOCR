namespace DocumentOCR.Application.Credits;

/// <summary>Prepaid-credit pricing/limits. Bound from the "Credits" config section.</summary>
public sealed class CreditOptions
{
    public const string SectionName = "Credits";

    /// <summary>Credits charged for the structured (TT78 XML) fast path.</summary>
    public int XmlParse { get; set; } = 1;

    /// <summary>Credits charged for the OCR pipeline (PDF/JPG/PNG).</summary>
    public int OcrExtraction { get; set; } = 2;

    /// <summary>
    /// Max credits a single organization may consume in one UTC calendar day, regardless of
    /// remaining balance. 0 or negative disables the cap.
    /// </summary>
    public int MaxDailyConsumePerOrg { get; set; }
}
