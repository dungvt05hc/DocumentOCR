namespace DocumentOCR.Application.Credits;

/// <summary>
/// Resolves how many credits a document processing attempt costs, based on which pipeline path
/// it will take (structured XML fast path vs. OCR). Shared by the upload/reprocess charge points
/// and the processing-failure refund point, so both always agree on the price.
/// </summary>
public static class CreditPricing
{
    public static int ResolveCost(string contentType, CreditOptions options) =>
        IsXmlContentType(contentType) ? options.XmlParse : options.OcrExtraction;

    private static bool IsXmlContentType(string contentType) =>
        string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase);
}
