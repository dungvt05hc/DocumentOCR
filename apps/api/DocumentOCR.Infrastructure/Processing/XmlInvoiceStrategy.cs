using System.Text;
using System.Text.Json;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Infrastructure.Ocr;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Processing;

/// <summary>
/// Thin <see cref="IDocumentExtractionStrategy"/> adapter over <see cref="IStructuredInvoiceParser"/>
/// (currently only <c>TT78XmlInvoiceParser</c>, in <c>Application/Processing</c> — pure XML parsing,
/// no infrastructure dependency). Lives in Infrastructure (not next to the parser) because it owns
/// the raw-XML artifact persistence that used to live in
/// <c>DocumentProcessingService.ProcessStructuredInvoiceAsync</c>, a real storage/config dependency.
/// </summary>
public sealed class XmlInvoiceStrategy : IDocumentExtractionStrategy
{
    private readonly IStructuredInvoiceParser _parser;
    private readonly IDocumentStorageService _storage;
    private readonly OcrOptions _options;

    public string Name => "XmlInvoiceStrategy";

    public int Priority => 0;

    public XmlInvoiceStrategy(IStructuredInvoiceParser parser, IDocumentStorageService storage, IOptions<OcrOptions> options)
    {
        _parser = parser;
        _storage = storage;
        _options = options.Value;
    }

    public Task<bool> CanHandleAsync(DocumentInput input, CancellationToken ct = default) =>
        Task.FromResult(_parser.CanParse(input.ContentType, input.FileName));

    public async Task<StructuredExtractionResult> ExtractAsync(DocumentInput input, CancellationToken ct = default)
    {
        var result = await _parser.ParseAsync(input.Content, ct);

        if (!_options.StoreRawProviderResponse || string.IsNullOrWhiteSpace(result.RawSourceText))
            return result;

        var rawXmlPath = await PersistRawXmlArtifactAsync(result.RawSourceText, ct);

        return result with
        {
            RawResponseJson = JsonSerializer.Serialize(result.RawSourceText),
            RawResponsePath = rawXmlPath
        };
    }

    private async Task<string> PersistRawXmlArtifactAsync(string rawXml, CancellationToken ct)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rawXml));
        return await _storage.SaveAsync(stream, $"{Guid.NewGuid()}-source.xml", "application/xml", ct);
    }
}
