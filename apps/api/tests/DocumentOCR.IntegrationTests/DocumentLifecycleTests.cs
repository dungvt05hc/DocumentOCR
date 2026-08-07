using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;
using DocumentOCR.Application.DTOs;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DocumentOCR.IntegrationTests;

/// <summary>
/// Drives the full MVP workflow through the real WebApi host — upload, process (Fake OCR),
/// review, export — proving that Domain/Application/Infrastructure/WebApi are wired together
/// correctly via DI (real controllers, real DbContext, real extraction/normalization/validation
/// pipeline). Only Postgres, Azure OCR, and the Hangfire worker are swapped for local test doubles.
/// </summary>
public class DocumentLifecycleTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    // Mirrors the server: ASP.NET Core's MVC JSON formatter defaults to Web conventions
    // (camelCase, case-insensitive matching) and Program.cs adds JsonStringEnumConverter so
    // enums serialize as strings. HttpContent.ReadFromJsonAsync/GetFromJsonAsync without an
    // explicit options argument already assumes JsonSerializerDefaults.Web, but doesn't know
    // about the enum converter, so "Uploaded" fails to parse into DocumentStatus without this.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public DocumentLifecycleTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UploadProcessReviewExport_FullLifecycle_WiresAllLayersTogether()
    {
        var client = _factory.CreateClient();

        // ── 1. Upload ────────────────────────────────────────────────────────────
        using var uploadContent = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }; // %PDF-1.4
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        uploadContent.Add(fileContent, "files", "invoice.pdf");

        var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);

        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
        var uploadResults = await uploadResponse.Content.ReadFromJsonAsync<List<UploadFileResult>>(JsonOptions);
        var uploadResult = Assert.Single(uploadResults!);
        Assert.True(uploadResult.Success, uploadResult.Error);
        Assert.NotNull(uploadResult.Document);
        Assert.False(string.IsNullOrWhiteSpace(uploadResult.JobId));
        var documentId = uploadResult.Document!.Id;

        // The job client is a recording fake (no real Hangfire storage in tests), so the
        // controller→IBackgroundJobClient wiring is verified by inspecting what it captured.
        var jobClient = Assert.IsType<RecordingBackgroundJobClient>(_factory.Services.GetRequiredService<Hangfire.IBackgroundJobClient>());
        Assert.Contains(documentId, jobClient.EnqueuedDocumentIds);

        // ── 2. Get — freshly uploaded, not yet processed ────────────────────────
        var afterUpload = await client.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{documentId}", JsonOptions);
        Assert.NotNull(afterUpload);
        Assert.Equal(DocumentStatus.Uploaded, afterUpload!.Status);
        Assert.Empty(afterUpload.Fields);

        // ── 3. Process (Fake OCR) ────────────────────────────────────────────────
        // Drives the same DocumentProcessingJob a real Hangfire worker would run, resolved
        // from the app's own DI container so the full pipeline (storage → OCR → extraction →
        // normalization → validation → persistence) executes exactly as in production.
        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<DocumentProcessingJob>();
            await job.ProcessDocumentAsync(documentId);
        }

        // ── 4. Fields returned ───────────────────────────────────────────────────
        var afterProcessing = await client.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{documentId}", JsonOptions);
        Assert.NotNull(afterProcessing);
        Assert.Equal(DocumentStatus.Processed, afterProcessing!.Status);
        Assert.Equal(1, afterProcessing.PageCount);
        // FakeOcrProvider has no explicit currency field, so the Vietnamese-grouped-money
        // fallback heuristic (FieldExtractionService.AddDefaultCurrencyCandidate) infers VND at
        // a deliberately low 0.5 confidence — that's expected to surface as an Info-level
        // LOW_CONFIDENCE warning, not a defect. Data-quality warnings (Warning/High/Error) must
        // still be absent for this clean sample document.
        Assert.DoesNotContain(afterProcessing.Warnings, w => w.Severity != ValidationSeverity.Info);
        Assert.NotNull(afterProcessing.OcrLog);
        Assert.Equal("Fake", afterProcessing.OcrLog!.ProviderName);

        AssertField(afterProcessing.Fields, FieldName.SupplierName, "CÔNG TY TNHH ABC");
        AssertField(afterProcessing.Fields, FieldName.SupplierTaxCode, "0100109106");
        AssertField(afterProcessing.Fields, FieldName.InvoiceNumber, "0001234");
        AssertField(afterProcessing.Fields, FieldName.TotalAmount, "1236767");

        // ── 5. Update fields (user review) ──────────────────────────────────────
        var updateRequest = new UpdateFieldsRequest
        {
            Fields = [new FieldUpdateItem { FieldName = nameof(FieldName.SupplierName), NormalizedValue = "CONG TY TNHH ABC (corrected)" }]
        };
        var updateResponse = await client.PutAsJsonAsync($"/api/documents/{documentId}/fields", updateRequest);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var afterReview = await client.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{documentId}", JsonOptions);
        Assert.NotNull(afterReview);
        Assert.Equal(DocumentStatus.Reviewed, afterReview!.Status);
        var supplierName = Assert.Single(afterReview.Fields, f => f.FieldName == nameof(FieldName.SupplierName));
        Assert.Equal("CONG TY TNHH ABC (corrected)", supplierName.NormalizedValue);
        Assert.True(supplierName.IsEditedByUser);

        // ── 6. Export to Excel ───────────────────────────────────────────────────
        var exportResponse = await client.PostAsJsonAsync("/api/exports/excel", new ExportRequest { DocumentIds = [documentId] });

        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            exportResponse.Content.Headers.ContentType?.MediaType);

        var excelBytes = await exportResponse.Content.ReadAsByteArrayAsync();
        using var workbook = new XLWorkbook(new MemoryStream(excelBytes));
        var documentsSheet = workbook.Worksheet("Documents");
        Assert.Equal("invoice.pdf", documentsSheet.Cell(2, 1).GetString());
        Assert.Equal("CONG TY TNHH ABC (corrected)", documentsSheet.Cell(2, 3).GetString());
    }

    [Fact]
    public async Task UploadProcessReview_Tt78XmlInvoice_SkipsOcrAndExtractsFieldsFromXml()
    {
        var client = _factory.CreateClient();

        // ── 1. Upload ────────────────────────────────────────────────────────────
        var xmlBytes = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "tt78", "valid-invoice.xml"));

        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(xmlBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
        uploadContent.Add(fileContent, "files", "invoice.xml");

        var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);

        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
        var uploadResults = await uploadResponse.Content.ReadFromJsonAsync<List<UploadFileResult>>(JsonOptions);
        var uploadResult = Assert.Single(uploadResults!);
        Assert.True(uploadResult.Success, uploadResult.Error);
        var documentId = uploadResult.Document!.Id;

        // ── 2. Process — drives the real DI-resolved pipeline, including the real
        // IStructuredInvoiceParser (TT78XmlInvoiceParser), same as a real Hangfire worker would.
        using (var scope = _factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<DocumentProcessingJob>();
            await job.ProcessDocumentAsync(documentId);
        }

        // ── 3. Processed via the structured XML parser, not OCR ─────────────────
        var afterProcessing = await client.GetFromJsonAsync<DocumentDetailDto>($"/api/documents/{documentId}", JsonOptions);
        Assert.NotNull(afterProcessing);
        Assert.Equal(DocumentStatus.Processed, afterProcessing!.Status);
        Assert.Equal(1, afterProcessing.PageCount);
        Assert.NotNull(afterProcessing.OcrLog);
        Assert.Equal("TT78Xml", afterProcessing.OcrLog!.ProviderName);

        AssertField(afterProcessing.Fields, FieldName.InvoiceNumber, "00001234");
        AssertField(afterProcessing.Fields, FieldName.SupplierTaxCode, "0100109106");
        AssertField(afterProcessing.Fields, FieldName.TotalAmount, "1236767");
        Assert.All(afterProcessing.Fields, f => Assert.Equal(1.0, f.Confidence));

        // ── 4. Review response resolves a full section/field breakdown ──────────
        var reviewResponse = await client.GetAsync($"/api/documents/{documentId}/review");
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
    }

    [Fact]
    public async Task Upload_XmlFileWithInvalidContent_IsRejected()
    {
        var client = _factory.CreateClient();

        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("this is not xml at all"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
        uploadContent.Add(fileContent, "files", "invoice.xml");

        var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);

        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
        var uploadResults = await uploadResponse.Content.ReadFromJsonAsync<List<UploadFileResult>>(JsonOptions);
        var uploadResult = Assert.Single(uploadResults!);
        Assert.False(uploadResult.Success);
        Assert.False(string.IsNullOrWhiteSpace(uploadResult.Error));
    }

    [Theory]
    [InlineData("text/xml")]
    [InlineData("application/xml")]
    [InlineData("application/octet-stream")]
    [InlineData(null)] // browsers/OSes sometimes send no Content-Type header at all for .xml
    public async Task Upload_ValidXmlWithVariousContentTypes_Succeeds(string? contentType)
    {
        var client = _factory.CreateClient();

        var xmlBytes = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "tt78", "valid-invoice.xml"));

        using var uploadContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(xmlBytes);
        if (contentType is not null)
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        uploadContent.Add(fileContent, "files", "invoice.xml");

        var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);

        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
        var uploadResults = await uploadResponse.Content.ReadFromJsonAsync<List<UploadFileResult>>(JsonOptions);
        var uploadResult = Assert.Single(uploadResults!);
        Assert.True(uploadResult.Success, uploadResult.Error);
    }

    [Fact]
    public async Task Upload_PdfRenamedToXmlExtension_IsRejected()
    {
        var client = _factory.CreateClient();

        using var uploadContent = new MultipartFormDataContent();
        var fileBytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34 }; // %PDF-1.4
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
        uploadContent.Add(fileContent, "files", "invoice.xml");

        var uploadResponse = await client.PostAsync("/api/documents/upload", uploadContent);

        Assert.Equal(HttpStatusCode.Accepted, uploadResponse.StatusCode);
        var uploadResults = await uploadResponse.Content.ReadFromJsonAsync<List<UploadFileResult>>(JsonOptions);
        var uploadResult = Assert.Single(uploadResults!);
        Assert.False(uploadResult.Success);
        Assert.False(string.IsNullOrWhiteSpace(uploadResult.Error));
    }

    [Fact]
    public async Task GetById_UnknownDocument_ReturnsNotFound()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static void AssertField(IEnumerable<ExtractedFieldDto> fields, FieldName fieldName, string expectedNormalizedValue)
    {
        var field = Assert.Single(fields, f => f.FieldName == fieldName.ToString());
        Assert.Equal(expectedNormalizedValue, field.NormalizedValue);
    }
}
