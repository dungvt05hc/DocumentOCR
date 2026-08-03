using System.Xml;
using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Services;
using DocumentOCR.Domain.Common;
using DocumentOCR.Infrastructure.Jobs;
using DocumentOCR.Infrastructure.Ocr;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DocumentOCR.WebApi.Controllers;

[ApiController]
[Route("api/documents")]
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documentService;
    private readonly IBackgroundJobClient _jobs;
    private readonly ILogger<DocumentsController> _logger;
    private readonly OcrDebugOptions _ocrDebugOptions;

    // For MVP, we use a fixed organization. In a real multi-tenant app,
    // this would come from the authenticated user's claims.
    private static readonly Guid DefaultOrganizationId = DefaultOrganization.Id;

    private static readonly Dictionary<string, string[]> AllowedExtensionsByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = [".pdf"],
            ["image/jpeg"] = [".jpg", ".jpeg"],
            ["image/png"] = [".png"],
            ["text/xml"] = [".xml"],
            ["application/xml"] = [".xml"]
        };

    // Magic-byte signatures for the binary content types. Client-supplied Content-Type and file
    // extension are trivially spoofable, so we also verify the actual file bytes before
    // persisting anything to storage. XML has no fixed magic bytes — see
    // ValidateXmlRootElementAsync for its own (content-type-specific) validation strategy.
    private static readonly Dictionary<string, byte[]> MagicBytesByContentType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = [0x25, 0x50, 0x44, 0x46], // %PDF
            ["image/jpeg"] = [0xFF, 0xD8, 0xFF],
            ["image/png"] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]
        };

    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB (PDF/JPG/PNG)
    private const long MaxXmlFileSizeBytes = 5 * 1024 * 1024; // 5 MB (structured TT78 XML invoices)
    private const int XmlValidationPeekBytes = 64 * 1024; // only peek at the start — never load the whole file to validate

    public DocumentsController(
        DocumentService documentService,
        IBackgroundJobClient jobs,
        ILogger<DocumentsController> logger,
        IOptions<OcrDebugOptions> ocrDebugOptions)
    {
        _documentService = documentService;
        _jobs = jobs;
        _logger = logger;
        _ocrDebugOptions = ocrDebugOptions.Value;
    }

    // POST /api/documents/upload
    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSizeBytes * 5)] // allow up to 5 files in one request
    [EnableRateLimiting("OcrProcessing")]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFileCollection files,
        CancellationToken ct)
    {
        if (files is null || files.Count == 0)
            return BadRequest(new { error = "No files provided." });

        // Each file is validated and persisted independently: an invalid or failing
        // file must not block, or silently discard, the others in the same batch.
        var results = new List<UploadFileResult>();
        foreach (var file in files)
            results.Add(await UploadSingleFileAsync(file, ct));

        return Accepted(results);
    }

    private async Task<UploadFileResult> UploadSingleFileAsync(IFormFile file, CancellationToken ct)
    {
        var validationError = ValidateUpload(file) ?? await ValidateFileSignatureAsync(file, ct);
        if (validationError is not null)
            return new UploadFileResult { FileName = file.FileName, Success = false, Error = validationError };

        try
        {
            await using var stream = file.OpenReadStream();
            var dto = await _documentService.UploadAsync(
                stream,
                file.FileName,
                file.ContentType,
                file.Length,
                DefaultOrganizationId,
                ct);

            var jobId = _jobs.Enqueue<DocumentProcessingJob>(
                job => job.ProcessDocumentAsync(dto.Id));

            _logger.LogInformation(
                "Uploaded document {DocumentId} ({FileName}, {ContentType}, {FileSizeBytes} bytes) and enqueued Hangfire job {JobId}",
                dto.Id,
                dto.OriginalFileName,
                dto.ContentType,
                dto.FileSizeBytes,
                jobId);

            return new UploadFileResult
            {
                FileName = file.FileName,
                Success = true,
                Document = dto,
                JobId = jobId
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save uploaded file '{FileName}'", file.FileName);
            return new UploadFileResult
            {
                FileName = file.FileName,
                Success = false,
                Error = "Failed to save the uploaded file. Please try again."
            };
        }
    }

    // GET /api/documents
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var docs = await _documentService.GetAllAsync(DefaultOrganizationId, ct);
        return Ok(docs);
    }

    // GET /api/documents/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var doc = await _documentService.GetByIdAsync(id, DefaultOrganizationId, ct);
        if (doc is null) return NotFound(new { error = $"Document {id} not found." });
        return Ok(doc);
    }

    // GET /api/documents/{id}/review — dynamic, document-category-driven review response
    [HttpGet("{id:guid}/review")]
    public async Task<IActionResult> GetReview(Guid id, CancellationToken ct)
    {
        var review = await _documentService.GetReviewByIdAsync(id, DefaultOrganizationId, _ocrDebugOptions.Enabled, ct);
        if (review is null) return NotFound(new { error = $"Document {id} not found." });
        return Ok(review);
    }

    // GET /api/documents/{id}/ocr-debug — development/debug view of the underlying OCR output.
    // Disabled (404) unless OcrDebug:Enabled is set; never shown by default in production.
    [HttpGet("{id:guid}/ocr-debug")]
    public async Task<IActionResult> GetOcrDebug(Guid id, CancellationToken ct)
    {
        if (!_ocrDebugOptions.Enabled) return NotFound();

        var debug = await _documentService.GetOcrDebugAsync(id, DefaultOrganizationId, _ocrDebugOptions.ExposeRawJson, ct);
        if (debug is null) return NotFound(new { error = $"Document {id} not found." });
        return Ok(debug);
    }

    // POST /api/documents/{id}/process  — re-trigger OCR
    [HttpPost("{id:guid}/process")]
    [EnableRateLimiting("OcrProcessing")]
    public async Task<IActionResult> Process(Guid id, CancellationToken ct)
    {
        await _documentService.MarkUploadedForProcessingAsync(id, DefaultOrganizationId, ct);

        var jobId = _jobs.Enqueue<DocumentProcessingJob>(
            job => job.ProcessDocumentAsync(id));

        _logger.LogInformation(
            "Manually enqueued processing for document {DocumentId} as Hangfire job {JobId}",
            id,
            jobId);

        return Accepted(new { documentId = id, jobId, message = "Processing enqueued." });
    }

    // PUT /api/documents/{id}/fields  — save user corrections
    [HttpPut("{id:guid}/fields")]
    public async Task<IActionResult> UpdateFields(
        Guid id,
        [FromBody] UpdateFieldsRequest request,
        CancellationToken ct)
    {
        if (request is null || (request.Fields.Count == 0 && request.Tables.Count == 0 && request.LineItems.Count == 0))
            return BadRequest(new { error = "No field, table, or line item updates provided." });

        await _documentService.UpdateFieldsAsync(id, DefaultOrganizationId, request, ct);
        return NoContent();
    }

    // GET /api/documents/{id}/download-original
    [HttpGet("{id:guid}/download-original")]
    public async Task<IActionResult> DownloadOriginal(Guid id, CancellationToken ct)
    {
        var (stream, contentType, fileName) = await _documentService.DownloadOriginalAsync(id, DefaultOrganizationId, ct);

        // Sanitize filename to prevent Content-Disposition injection
        var safeFileName = Path.GetFileName(fileName);
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{safeFileName}\"");
        return File(stream, contentType);
    }

    private static bool IsXmlContentType(string contentType) =>
        string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase);

    private static long GetMaxFileSizeBytes(string contentType) =>
        IsXmlContentType(contentType) ? MaxXmlFileSizeBytes : MaxFileSizeBytes;

    private static string? ValidateUpload(IFormFile file)
    {
        if (file.Length == 0)
            return "File is empty.";

        if (file.Length > GetMaxFileSizeBytes(file.ContentType))
        {
            return IsXmlContentType(file.ContentType)
                ? "File exceeds the 5 MB limit for XML invoices."
                : "File exceeds the 20 MB limit.";
        }

        if (!AllowedExtensionsByContentType.TryGetValue(file.ContentType, out var allowedExtensions))
            return $"File type '{file.ContentType}' is not supported. Allowed: PDF, JPEG, PNG, XML.";

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension)
            || !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return $"File extension '{extension}' does not match content type '{file.ContentType}'.";
        }

        return null;
    }

    // Each supported content type gets its own validation strategy: magic bytes for the binary
    // formats, and a bounded well-formedness/root-element check for XML (which has no fixed
    // magic bytes). Dispatching explicitly here — instead of indexing a single shared map —
    // avoids a KeyNotFoundException for any content type that doesn't have magic bytes.
    private static async Task<string?> ValidateFileSignatureAsync(IFormFile file, CancellationToken ct) =>
        IsXmlContentType(file.ContentType)
            ? await ValidateXmlRootElementAsync(file, ct)
            : await ValidateMagicBytesAsync(file, ct);

    private static async Task<string?> ValidateMagicBytesAsync(IFormFile file, CancellationToken ct)
    {
        // ValidateUpload already guarantees file.ContentType is a key in this map for every
        // non-XML content type it allows.
        var signature = MagicBytesByContentType[file.ContentType];

        var header = new byte[signature.Length];
        await using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length), ct);

        if (bytesRead < signature.Length || !header.AsSpan(0, signature.Length).SequenceEqual(signature))
            return $"File does not match the expected format for '{file.ContentType}'.";

        return null;
    }

    private static async Task<string?> ValidateXmlRootElementAsync(IFormFile file, CancellationToken ct)
    {
        var buffer = new byte[XmlValidationPeekBytes];
        await using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);

        if (bytesRead == 0)
            return "File does not contain valid XML content.";

        // Never resolve DTDs/external entities on user-supplied XML (XXE protection).
        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            Async = true
        };

        try
        {
            using var peekStream = new MemoryStream(buffer, 0, bytesRead);
            using var reader = XmlReader.Create(peekStream, readerSettings);

            // The buffer is deliberately truncated at XmlValidationPeekBytes, so the document
            // does not need to be well-formed to its closing tag — MoveToContentAsync only needs
            // to reach the first real node before EOF to confirm a genuine root element exists.
            if (await reader.MoveToContentAsync() != XmlNodeType.Element)
                return "File does not contain a valid XML root element.";
        }
        catch (XmlException)
        {
            return "File does not contain valid XML content.";
        }

        return null;
    }
}
