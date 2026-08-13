namespace DocumentOCR.Application.Credits;

/// <summary>
/// Resolves how many credits a document processing attempt costs. <see cref="ResolveCost"/> is
/// the pre-processing estimate used to pre-charge/hold credit before processing runs (and to
/// refund on permanent failure) — shared by the upload/reprocess charge points and the
/// processing-failure refund point, so both always agree on the price. Which concrete path a PDF
/// actually takes (text-layer read vs. real OCR) is only known once
/// <see cref="Interfaces.IDocumentOcrProvider"/> has run, so <see cref="ResolveActualCost"/>
/// resolves the true price from the provider name that actually served the request — used only
/// for the post-processing reconciliation refund in <c>DocumentProcessingService</c>.
/// </summary>
public static class CreditPricing
{
    public const string PdfTextLayerProviderName = "PdfTextLayer";
    public const string PdfTextLayerLlmProviderName = "PdfTextLayerLlm";
    public const string StructuredXmlProviderName = "TT78Xml";

    /// <summary>
    /// Pre-processing price estimate. Mirrors <c>TT78XmlInvoiceParser.CanParse</c>'s XML
    /// detection (extension OR content-type) rather than content-type alone, so a genuine .xml
    /// upload with an empty/generic Content-Type — which <c>DocumentsController</c> accepts and
    /// which will in fact take the free structured-XML path — is pre-charged at the XML price,
    /// not mistakenly held at the OCR price.
    /// </summary>
    public static int ResolveCost(string contentType, string fileName, CreditOptions options) =>
        IsXmlUpload(contentType, fileName) ? options.XmlParse : options.OcrExtraction;

    /// <summary>
    /// The price that actually applies once the pipeline knows which provider served the
    /// request — as opposed to <see cref="ResolveCost"/>'s pre-processing estimate (which for a
    /// PDF is always the OCR price, since text-layer eligibility isn't knowable until the
    /// provider runs).
    /// </summary>
    public static int ResolveActualCost(string providerName, CreditOptions options) => providerName switch
    {
        StructuredXmlProviderName => options.XmlParse,
        PdfTextLayerProviderName => options.PdfTextLayer,
        PdfTextLayerLlmProviderName => options.PdfTextLayerLlm,
        _ => options.OcrExtraction
    };

    /// <summary>Same XML-detection rule as <c>TT78XmlInvoiceParser.CanParse</c> — kept in sync deliberately.</summary>
    public static bool IsXmlUpload(string contentType, string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".xml", StringComparison.OrdinalIgnoreCase)
        || IsXmlContentType(contentType);

    private static bool IsXmlContentType(string contentType) =>
        string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase);
}
