using System.Text.Json;
using ClosedXML.Excel;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Export;
using DocumentOCR.Infrastructure.Persistence;
using DocumentOCR.Infrastructure.Processing;
using DocumentOCR.Infrastructure.Profiles;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DocumentOCR.UnitTests.Export;

public class ClosedXmlExportServiceTests
{
    [Fact]
    public async Task ExportAsync_SelectedDocuments_CreatesDocumentsAndWarningsSheets()
    {
        await using var db = CreateDbContext();
        var (firstDocument, secondDocument) = await SeedDocumentsAsync(db);
        var sut = new ClosedXmlExportService(db, new DocumentProfileCatalog(), new ReviewTableBuilder());

        var bytes = await sut.ExportAsync([firstDocument.Id, secondDocument.Id]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Contains(workbook.Worksheets, sheet => sheet.Name == "Documents");
        Assert.Contains(workbook.Worksheets, sheet => sheet.Name == "Warnings");
    }

    [Fact]
    public async Task ExportAsync_SelectedDocuments_WritesVietnameseHeaders()
    {
        await using var db = CreateDbContext();
        var (firstDocument, secondDocument) = await SeedDocumentsAsync(db);
        var sut = new ClosedXmlExportService(db, new DocumentProfileCatalog(), new ReviewTableBuilder());

        var bytes = await sut.ExportAsync([firstDocument.Id, secondDocument.Id]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var documents = workbook.Worksheet("Documents");
        Assert.Equal("Tên tệp", documents.Cell(1, 1).GetString());
        Assert.Equal("Loại chứng từ", documents.Cell(1, 2).GetString());
        Assert.Equal("Tên nhà cung cấp", documents.Cell(1, 3).GetString());
        Assert.Equal("Tổng thanh toán", documents.Cell(1, 9).GetString());
        Assert.Equal("Trạng thái duyệt", documents.Cell(1, 12).GetString());

        var warnings = workbook.Worksheet("Warnings");
        Assert.Equal("Tên tệp", warnings.Cell(1, 1).GetString());
        Assert.Equal("Tên trường", warnings.Cell(1, 2).GetString());
        Assert.Equal("Mức độ", warnings.Cell(1, 5).GetString());
    }

    [Fact]
    public async Task ExportAsync_FieldValues_WritesOneRowPerDocument()
    {
        await using var db = CreateDbContext();
        var (firstDocument, secondDocument) = await SeedDocumentsAsync(db);
        var sut = new ClosedXmlExportService(db, new DocumentProfileCatalog(), new ReviewTableBuilder());

        var bytes = await sut.ExportAsync([firstDocument.Id, secondDocument.Id]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Documents");

        Assert.Equal("invoice-a.pdf", sheet.Cell(2, 1).GetString());
        Assert.Equal("Invoice", sheet.Cell(2, 2).GetString());
        Assert.Equal("CONG TY TNHH ABC", sheet.Cell(2, 3).GetString());
        Assert.Equal("0100109106", sheet.Cell(2, 4).GetString());
        Assert.Equal("AA/24E-0001234", sheet.Cell(2, 5).GetString());
        Assert.Equal("VND", sheet.Cell(2, 10).GetString());
        Assert.Equal(2, sheet.Cell(2, 11).GetValue<int>());
        Assert.Equal("Đã duyệt", sheet.Cell(2, 12).GetString());

        Assert.Equal("receipt-b.png", sheet.Cell(3, 1).GetString());
        Assert.Equal("Receipt", sheet.Cell(3, 2).GetString());
        Assert.Equal("Chưa duyệt", sheet.Cell(3, 12).GetString());
    }

    [Fact]
    public async Task ExportAsync_MoneyAndDateFields_UsesExcelFormatting()
    {
        await using var db = CreateDbContext();
        var (firstDocument, _) = await SeedDocumentsAsync(db);
        var sut = new ClosedXmlExportService(db, new DocumentProfileCatalog(), new ReviewTableBuilder());

        var bytes = await sut.ExportAsync([firstDocument.Id]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Documents");

        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 6).DataType);
        Assert.Equal("dd/MM/yyyy", sheet.Cell(2, 6).Style.DateFormat.Format);
        Assert.Equal(new DateTime(2026, 7, 1), sheet.Cell(2, 6).GetDateTime());

        Assert.Equal(XLDataType.Number, sheet.Cell(2, 7).DataType);
        Assert.Equal(1000000m, sheet.Cell(2, 7).GetValue<decimal>());
        Assert.Equal("#,##0", sheet.Cell(2, 7).Style.NumberFormat.Format);
        Assert.Equal(1100000m, sheet.Cell(2, 9).GetValue<decimal>());
    }

    [Fact]
    public async Task ExportAsync_DocumentWarnings_WritesWarningsSheetRows()
    {
        await using var db = CreateDbContext();
        var (firstDocument, secondDocument) = await SeedDocumentsAsync(db);
        var sut = new ClosedXmlExportService(db, new DocumentProfileCatalog(), new ReviewTableBuilder());

        var bytes = await sut.ExportAsync([firstDocument.Id, secondDocument.Id]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Warnings");

        Assert.Equal("invoice-a.pdf", sheet.Cell(2, 1).GetString());
        Assert.Equal(nameof(FieldName.SupplierTaxCode), sheet.Cell(2, 2).GetString());
        Assert.Equal("INVALID_TAX_CODE_LENGTH", sheet.Cell(2, 3).GetString());
        Assert.Equal("Tax code length is invalid.", sheet.Cell(2, 4).GetString());
        Assert.Equal("Warning", sheet.Cell(2, 5).GetString());

        Assert.Equal("receipt-b.png", sheet.Cell(4, 1).GetString());
        Assert.Equal(nameof(FieldName.TotalAmount), sheet.Cell(4, 2).GetString());
        Assert.Equal("LOW_CONFIDENCE", sheet.Cell(4, 3).GetString());
        Assert.Equal("Info", sheet.Cell(4, 5).GetString());
    }

    [Fact]
    public async Task ExportAsync_DocumentWithOnlyAliasFieldKey_StillPopulatesCanonicalColumn()
    {
        // "MerchantName" is a review-profile field key (see IDocumentProfileCatalog's PosReceipt
        // profile) that aliases the legacy "SupplierName" — a document saved with only the alias
        // key populated (e.g. because the user filled it in via the dynamic review UI) must still
        // show up under the "Tên nhà cung cấp" (SupplierName) column, not be left blank.
        await using var db = CreateDbContext();
        var organization = new Organization { Name = "Alias Test Organization", Slug = "alias-test-organization" };
        var document = new Document
        {
            OrganizationId = organization.Id,
            OriginalFileName = "receipt-c.png",
            StoredFilePath = "2026/07/receipt-c.png",
            ContentType = "image/png",
            FileSizeBytes = 512,
            Status = DocumentStatus.Processed,
            DocumentType = DocumentType.PosReceipt
        };
        document.Fields.Add(new ExtractedField
        {
            DocumentId = document.Id,
            FieldName = "MerchantName",
            RawValue = "MOTA CAFE",
            NormalizedValue = "MOTA CAFE",
            Confidence = 0.9,
            IsEditedByUser = true
        });

        db.Organizations.Add(organization);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var sut = new ClosedXmlExportService(db, new DocumentProfileCatalog(), new ReviewTableBuilder());
        var bytes = await sut.ExportAsync([document.Id]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Documents");
        Assert.Equal("MOTA CAFE", sheet.Cell(2, 3).GetString());
    }

    [Fact]
    public async Task ExportAsync_DocumentWithDetectedTable_WritesTablesSheet()
    {
        await using var db = CreateDbContext();
        var organization = new Organization { Name = "Tables Test Organization", Slug = "tables-test-organization" };
        var document = new Document
        {
            OrganizationId = organization.Id,
            OriginalFileName = "invoice-with-table.pdf",
            StoredFilePath = "2026/07/invoice-with-table.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Status = DocumentStatus.Processed,
            DocumentType = DocumentType.Invoice,
            TablesJson = JsonSerializer.Serialize(new List<OcrTable>
            {
                new()
                {
                    RowCount = 2,
                    ColumnCount = 3,
                    Cells =
                    [
                        new() { RowIndex = 0, ColumnIndex = 0, Text = "ITEMS", Kind = "columnHeader" },
                        new() { RowIndex = 0, ColumnIndex = 1, Text = "QUANTITY", Kind = "columnHeader" },
                        new() { RowIndex = 0, ColumnIndex = 2, Text = "PRICE", Kind = "columnHeader" },
                        new() { RowIndex = 1, ColumnIndex = 0, Text = "Widget" },
                        new() { RowIndex = 1, ColumnIndex = 1, Text = "2" },
                        new() { RowIndex = 1, ColumnIndex = 2, Text = "10.00" }
                    ]
                }
            })
        };

        db.Organizations.Add(organization);
        db.Documents.Add(document);
        await db.SaveChangesAsync();

        var sut = new ClosedXmlExportService(db, new DocumentProfileCatalog(), new ReviewTableBuilder());
        var bytes = await sut.ExportAsync([document.Id]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Contains(workbook.Worksheets, sheet => sheet.Name == "Tables");

        var sheet = workbook.Worksheet("Tables");
        Assert.Equal("invoice-with-table.pdf", sheet.Cell(2, 1).GetString());
        Assert.Equal("table-0", sheet.Cell(2, 2).GetString());
        // Row 2 is the header row (row index 0) — its cells hold the raw column labels.
        Assert.Equal("ITEMS", sheet.Cell(2, 5).GetString());
        Assert.Equal("Widget", sheet.Cell(3, 5).GetString());
    }

    [Fact]
    public async Task ExportAsync_DocumentWithNoTables_DoesNotFailAndTablesSheetHasNoRows()
    {
        await using var db = CreateDbContext();
        var (firstDocument, secondDocument) = await SeedDocumentsAsync(db);
        var sut = new ClosedXmlExportService(db, new DocumentProfileCatalog(), new ReviewTableBuilder());

        var bytes = await sut.ExportAsync([firstDocument.Id, secondDocument.Id]);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet("Tables");
        // Neither seeded document has TablesJson set — only the header row should exist.
        Assert.Equal(1, sheet.LastRowUsed()?.RowNumber() ?? 1);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static async Task<(Document FirstDocument, Document SecondDocument)> SeedDocumentsAsync(
        ApplicationDbContext db)
    {
        var organization = new Organization
        {
            Name = "Test Organization",
            Slug = "test-organization"
        };

        var firstDocument = new Document
        {
            OrganizationId = organization.Id,
            OriginalFileName = "invoice-a.pdf",
            StoredFilePath = "2026/07/invoice-a.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Status = DocumentStatus.Reviewed,
            DocumentType = DocumentType.Invoice,
            CreatedAt = new DateTime(2026, 7, 2, 9, 15, 0, DateTimeKind.Utc)
        };

        firstDocument.Fields.Add(Field(firstDocument.Id, FieldName.SupplierName, "CONG TY TNHH ABC"));
        firstDocument.Fields.Add(Field(firstDocument.Id, FieldName.SupplierTaxCode, "0100109106"));
        firstDocument.Fields.Add(Field(firstDocument.Id, FieldName.InvoiceNumber, "AA/24E-0001234"));
        firstDocument.Fields.Add(Field(firstDocument.Id, FieldName.InvoiceDate, "2026-07-01"));
        firstDocument.Fields.Add(Field(firstDocument.Id, FieldName.SubtotalAmount, "1000000"));
        firstDocument.Fields.Add(Field(firstDocument.Id, FieldName.VatAmount, "100000"));
        firstDocument.Fields.Add(Field(firstDocument.Id, FieldName.TotalAmount, "1100000"));
        firstDocument.Fields.Add(Field(firstDocument.Id, FieldName.Currency, "VND"));

        firstDocument.ValidationWarnings.Add(Warning(
            firstDocument.Id,
            FieldName.SupplierTaxCode,
            "INVALID_TAX_CODE_LENGTH",
            "Tax code length is invalid.",
            ValidationSeverity.Warning));
        firstDocument.ValidationWarnings.Add(Warning(
            firstDocument.Id,
            FieldName.TotalAmount,
            "AMOUNT_CONSISTENCY_MISMATCH",
            "Total amount does not match subtotal and VAT.",
            ValidationSeverity.High));

        var secondDocument = new Document
        {
            OrganizationId = organization.Id,
            OriginalFileName = "receipt-b.png",
            StoredFilePath = "2026/07/receipt-b.png",
            ContentType = "image/png",
            FileSizeBytes = 2048,
            Status = DocumentStatus.Processed,
            DocumentType = DocumentType.Receipt,
            CreatedAt = new DateTime(2026, 7, 3, 11, 0, 0, DateTimeKind.Utc)
        };

        secondDocument.Fields.Add(Field(secondDocument.Id, FieldName.SupplierName, "CUA HANG MINH AN"));
        secondDocument.Fields.Add(Field(secondDocument.Id, FieldName.InvoiceNumber, "RC-9988"));
        secondDocument.Fields.Add(Field(secondDocument.Id, FieldName.InvoiceDate, "03/07/2026"));
        secondDocument.Fields.Add(Field(secondDocument.Id, FieldName.TotalAmount, "₫1.234.567"));
        secondDocument.Fields.Add(Field(secondDocument.Id, FieldName.Currency, "VND"));
        secondDocument.ValidationWarnings.Add(Warning(
            secondDocument.Id,
            FieldName.TotalAmount,
            "LOW_CONFIDENCE",
            "Total amount has low confidence.",
            ValidationSeverity.Info));

        db.Organizations.Add(organization);
        db.Documents.AddRange(firstDocument, secondDocument);
        await db.SaveChangesAsync();

        return (firstDocument, secondDocument);
    }

    private static ExtractedField Field(Guid documentId, FieldName fieldName, string value) =>
        new()
        {
            DocumentId = documentId,
            FieldName = fieldName.ToString(),
            RawValue = value,
            NormalizedValue = value,
            Confidence = 0.95
        };

    private static ValidationWarning Warning(
        Guid documentId,
        FieldName fieldName,
        string code,
        string message,
        ValidationSeverity severity) =>
        new()
        {
            DocumentId = documentId,
            FieldName = fieldName.ToString(),
            WarningCode = code,
            Message = message,
            Severity = severity
        };
}
