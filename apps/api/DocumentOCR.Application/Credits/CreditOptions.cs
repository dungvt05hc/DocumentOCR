namespace DocumentOCR.Application.Credits;

/// <summary>Prepaid-credit pricing/limits. Bound from the "Credits" config section.</summary>
public sealed class CreditOptions
{
    public const string SectionName = "Credits";

    /// <summary>
    /// Master switch for the whole credit-charging layer. Defaults to <see langword="false"/> so
    /// local development never blocks on credit balance — <see cref="Interfaces.ICreditService.TryConsumeAsync"/>
    /// always succeeds without writing a ledger row while this is off. Set <see langword="true"/>
    /// to enforce prepaid balances (production).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Credits charged for the structured (TT78 XML) fast path.</summary>
    public int XmlParse { get; set; } = 1;

    /// <summary>Credits charged when a PDF's own embedded text layer is read directly (no OCR call).</summary>
    public int PdfTextLayer { get; set; } = 2;

    /// <summary>
    /// Credits charged when a PDF is processed via the text-layer + LLM path
    /// (<c>PdfTextLayerLlmStrategy</c>). Unlike <see cref="PdfTextLayer"/>, this path calls a paid
    /// LLM API, so it isn't free the way plain text-layer reading is — defaults to the same price
    /// as <see cref="OcrExtraction"/>.
    /// </summary>
    public int PdfTextLayerLlm { get; set; } = 2;

    /// <summary>Credits charged for the OCR pipeline (PDF/JPG/PNG, when text-layer reading doesn't apply).</summary>
    public int OcrExtraction { get; set; } = 2;

    /// <summary>
    /// Max credits a single organization may consume in one UTC calendar day, regardless of
    /// remaining balance. 0 or negative disables the cap.
    /// </summary>
    public int MaxDailyConsumePerOrg { get; set; }
}
