using System.Xml;
using DocumentOCR.Application.Credits;
using DocumentOCR.Application.DTOs;
using DocumentOCR.Application.Exceptions;
using DocumentOCR.Application.Interfaces;
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
    private readonly ICreditService _creditService;
    private readonly ILogger<DocumentsController> _logger;
    private readonly OcrDebugOptions _ocrDebugOptions;
    private readonly CreditOptions _creditOptions;

    // For MVP, we use a fixed organization. In a real multi-tenant app,
    // this would come from the authenticated user's claims.
    private static readonly Guid DefaultOrganizationId = DefaultOrganization.Id;

    private const long MaxFileSizeBytes = 20 * 1024 * 1024; // 20 MB (PDF/JPG/PNG)
    private const long MaxXmlFileSizeBytes = 5 * 1024 * 1024; // 5 MB (structured TT78 XML invoices)
    private const int XmlValidationPeekBytes = 64 * 1024; // only peek at the start — never load the whole file to validate

    private enum ContentValidationStrategy
    {
        MagicBytes,
        Xml
    }

    private sealed record UploadRule(
        string[] AcceptableContentTypes,
        long MaxSizeBytes,
        ContentValidationStrategy Strategy,
        byte[]? MagicBytes = null);

    // File EXTENSION is the primary validation key, not client-supplied Content-Type: browsers
    // and OSes send wildly inconsistent Content-Type values for the same file (.xml in
    // particular shows up as text/xml, application/xml, application/octet-stream, or empty
    // depending on the machine), so keying off it first would reject legitimate uploads.
    // AcceptableContentTypes is only a secondary cross-check — see ValidateUpload. XML has no
    // fixed magic bytes, so it gets its own strategy (ValidateXmlRootElementAsync) instead of a
    // byte signature.
    private static readonly Dictionary<string, UploadRule> AllowedUploads =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = new(["application/pdf"], MaxFileSizeBytes, ContentValidationStrategy.MagicBytes, [0x25, 0x50, 0x44, 0x46]), // %PDF
            [".jpg"] = new(["image/jpeg"], MaxFileSizeBytes, ContentValidationStrategy.MagicBytes, [0xFF, 0xD8, 0xFF]),
            [".jpeg"] = new(["image/jpeg"], MaxFileSizeBytes, ContentValidationStrategy.MagicBytes, [0xFF, 0xD8, 0xFF]),
            [".png"] = new(["image/png"], MaxFileSizeBytes, ContentValidationStrategy.MagicBytes, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            [".xml"] = new(["text/xml", "application/xml"], MaxXmlFileSizeBytes, ContentValidationStrategy.Xml)
        };

    public DocumentsController(
        DocumentService documentService,
        IBackgroundJobClient jobs,
        ICreditService creditService,
        ILogger<DocumentsController> logger,
        IOptions<OcrDebugOptions> ocrDebugOptions,
        IOptions<CreditOptions> creditOptions)
    {
        _documentService = documentService;
        _jobs = jobs;
        _creditService = creditService;
        _logger = logger;
        _ocrDebugOptions = ocrDebugOptions.Value;
        _creditOptions = creditOptions.Value;
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

            var cost = CreditPricing.ResolveCost(dto.ContentType, dto.OriginalFileName, _creditOptions);
            bool consumed;
            try
            {
                consumed = await _creditService.TryConsumeAsync(
                    DefaultOrganizationId, cost, "Document", dto.Id, "Document processing", ct);
            }
            catch (DailyCreditCapExceededException ex)
            {
                _logger.LogWarning(
                    "Not enqueuing document {DocumentId}: {Message}", dto.Id, ex.Message);
                await _documentService.MarkBlockedByCreditAsync(dto.Id, DefaultOrganizationId, ex.Message, ct);
                return new UploadFileResult
                {
                    FileName = file.FileName,
                    Success = false,
                    Error = ex.Message
                };
            }

            if (!consumed)
            {
                var balance = await _creditService.GetBalanceAsync(DefaultOrganizationId, ct);
                var message = $"Không đủ credit (còn {balance}). Nạp thêm để tiếp tục xử lý.";

                _logger.LogWarning(
                    "Not enqueuing document {DocumentId}: organization {OrganizationId} has insufficient credit balance for {Cost} credit(s)",
                    dto.Id, DefaultOrganizationId, cost);
                await _documentService.MarkBlockedByCreditAsync(dto.Id, DefaultOrganizationId, message, ct);
                return new UploadFileResult
                {
                    FileName = file.FileName,
                    Success = false,
                    Error = message
                };
            }

            var jobId = _jobs.Enqueue<DocumentProcessingJob>(
                job => job.ProcessDocumentAsync(dto.Id));

            _logger.LogInformation(
                "Uploaded document {DocumentId} ({FileName}, {ContentType}, {FileSizeBytes} bytes), charged {Cost} credit(s), and enqueued Hangfire job {JobId}",
                dto.Id,
                dto.OriginalFileName,
                dto.ContentType,
                dto.FileSizeBytes,
                cost,
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

    // GET /api/documents?clientProfileId=&from=&to=
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? clientProfileId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var docs = await _documentService.GetAllAsync(DefaultOrganizationId, clientProfileId, from, to, ct);
        return Ok(docs);
    }

    // PUT /api/documents/{id}/client — assign or unassign the client this document belongs to
    [HttpPut("{id:guid}/client")]
    public async Task<IActionResult> AssignClient(
        Guid id,
        [FromBody] AssignDocumentClientRequest request,
        CancellationToken ct)
    {
        await _documentService.AssignClientAsync(id, DefaultOrganizationId, request.ClientProfileId, ct);
        return NoContent();
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
        var doc = await _documentService.GetByIdAsync(id, DefaultOrganizationId, ct);
        if (doc is null) return NotFound(new { error = $"Document {id} not found." });

        // This endpoint is what a repeated-call spending attack would hit (unlike upload, it
        // has no file-storage cost to slow an attacker down), so it must charge credit before
        // enqueuing exactly like Upload does — otherwise it's an unlimited-retries burn loop
        // bounded only by the per-IP rate limiter, not by the org's actual credit balance.
        var cost = CreditPricing.ResolveCost(doc.ContentType, doc.OriginalFileName, _creditOptions);
        bool consumed;
        try
        {
            consumed = await _creditService.TryConsumeAsync(
                DefaultOrganizationId, cost, "Document", id, "Manual reprocess", ct);
        }
        catch (DailyCreditCapExceededException ex)
        {
            _logger.LogWarning("Not re-enqueuing document {DocumentId}: {Message}", id, ex.Message);
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = ex.Message });
        }

        if (!consumed)
        {
            var balance = await _creditService.GetBalanceAsync(DefaultOrganizationId, ct);
            var message = $"Không đủ credit (còn {balance}). Nạp thêm để tiếp tục xử lý.";

            _logger.LogWarning(
                "Not re-enqueuing document {DocumentId}: organization {OrganizationId} has insufficient credit balance for {Cost} credit(s)",
                id, DefaultOrganizationId, cost);
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = message });
        }

        await _documentService.MarkUploadedForProcessingAsync(id, DefaultOrganizationId, ct);

        var jobId = _jobs.Enqueue<DocumentProcessingJob>(
            job => job.ProcessDocumentAsync(id));

        _logger.LogInformation(
            "Manually enqueued processing for document {DocumentId} as Hangfire job {JobId}, charged {Cost} credit(s)",
            id,
            jobId,
            cost);

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

    private static string? ValidateUpload(IFormFile file)
    {
        if (file.Length == 0)
            return "File is empty.";

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedUploads.TryGetValue(extension, out var rule))
            return $"File extension '{extension}' is not supported. Allowed: XML, PDF, JPG, PNG.";

        if (file.Length > rule.MaxSizeBytes)
        {
            return rule.Strategy == ContentValidationStrategy.Xml
                ? "File exceeds the 5 MB limit for XML invoices."
                : "File exceeds the 20 MB limit.";
        }

        // Content-Type is only a secondary cross-check against the extension, never the primary
        // key: an empty value or the generic "application/octet-stream" (both common for .xml,
        // depending on the uploading browser/OS) are treated as "unknown" and skipped rather
        // than rejected. A non-empty, specific value that doesn't match the extension is still
        // rejected — that combination is either a spoofed upload or a genuine user mistake.
        if (!string.IsNullOrWhiteSpace(file.ContentType)
            && !string.Equals(file.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
            && !rule.AcceptableContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return $"Content type '{file.ContentType}' does not match file extension '{extension}'.";
        }

        return null;
    }

    // Each supported extension gets its own validation strategy: magic bytes for the binary
    // formats, and a bounded well-formedness/root-element check for XML (which has no fixed
    // magic bytes). ValidateUpload already guarantees the extension is a key in AllowedUploads
    // by the time this runs, but TryGetValue is still used defensively — an indexer here could
    // otherwise throw KeyNotFoundException for any extension without a magic-byte entry.
    private static async Task<string?> ValidateFileSignatureAsync(IFormFile file, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName);
        if (!AllowedUploads.TryGetValue(extension, out var rule))
            return $"File extension '{extension}' is not supported.";

        return rule.Strategy == ContentValidationStrategy.Xml
            ? await ValidateXmlRootElementAsync(file, ct)
            : await ValidateMagicBytesAsync(file, rule.MagicBytes!, extension, ct);
    }

    private static async Task<string?> ValidateMagicBytesAsync(
        IFormFile file, byte[] signature, string extension, CancellationToken ct)
    {
        var header = new byte[signature.Length];
        await using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length), ct);

        if (bytesRead < signature.Length || !header.AsSpan(0, signature.Length).SequenceEqual(signature))
            return $"File content does not match the expected format for '{extension}'.";

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
