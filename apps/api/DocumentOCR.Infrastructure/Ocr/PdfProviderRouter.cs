using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using Microsoft.Extensions.Logging;

namespace DocumentOCR.Infrastructure.Ocr;

/// <summary>
/// <see cref="IDocumentOcrProvider"/> registered as the pipeline's default provider. For PDF
/// uploads it tries <see cref="PdfTextLayerProvider"/> first (free, exact, for software-generated
/// e-invoices) and falls back to the configured OCR provider (typically Azure) when the PDF turns
/// out to be a scan or the text-layer read fails. Non-PDF uploads (JPG/PNG) are handed straight to
/// the OCR provider, unchanged from before this router existed.
/// </summary>
public sealed class PdfProviderRouter : IDocumentOcrProvider
{
    private const string PdfContentType = "application/pdf";

    // Depends on IDocumentOcrProvider (not the concrete PdfTextLayerProvider) so tests can swap in
    // a stub for the text-layer branch without needing a real PDF -- production DI still passes
    // the real PdfTextLayerProvider, which implements this interface like every other provider.
    private readonly IDocumentOcrProvider _pdfTextLayerProvider;
    private readonly IDocumentOcrProvider _ocrProvider;
    private readonly ILogger<PdfProviderRouter> _logger;

    public string ProviderName => "PdfProviderRouter";

    public PdfProviderRouter(
        IDocumentOcrProvider pdfTextLayerProvider,
        IDocumentOcrProvider ocrProvider,
        ILogger<PdfProviderRouter> logger)
    {
        _pdfTextLayerProvider = pdfTextLayerProvider;
        _ocrProvider = ocrProvider;
        _logger = logger;
    }

    public async Task<NormalizedOcrDocument> AnalyzeAsync(DocumentInput input, CancellationToken ct = default)
    {
        if (!string.Equals(input.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase))
            return await _ocrProvider.AnalyzeAsync(input, ct);

        input.Content.Position = 0;
        var textLayerResult = await _pdfTextLayerProvider.AnalyzeAsync(input, ct);

        if (textLayerResult.Success)
        {
            _logger.LogInformation(
                "PDF text layer branch selected for '{FileName}' -- {PageCount} page(s), OCR provider not called.",
                input.FileName, textLayerResult.PageCount);
            return textLayerResult;
        }

        _logger.LogInformation(
            "PDF text layer branch skipped for '{FileName}' ({Reason}) -- falling back to OCR provider {ProviderName}.",
            input.FileName, textLayerResult.ErrorMessage, _ocrProvider.ProviderName);

        input.Content.Position = 0;
        return await _ocrProvider.AnalyzeAsync(input, ct);
    }
}
