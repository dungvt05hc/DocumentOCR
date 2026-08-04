using DocumentOCR.Application.Models;
using DocumentOCR.Application.Processing;
using DocumentOCR.Application.Profiles;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;
using Xunit;

namespace DocumentOCR.UnitTests.Export;

public class AccountingReportBuilderTests
{
    private static readonly AccountingReportHeader Header = new("CONG TY TNHH ABC", "123 Le Loi, Q1", "0100109106", ClientType.HouseholdBusiness);
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    [Fact]
    public void Build_PurchaseInvoiceList_MultipleVatRates_GroupsAndSumsCorrectly()
    {
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());

        var docA = MakeDocument("a.pdf", invoiceDate: "2026-01-05", subtotal: "10000000", vat: "1000000", total: "11000000");
        var docB = MakeDocument("b.pdf", invoiceDate: "2026-01-10", subtotal: "5000000", vat: "500000", total: "5500000");
        var docC = MakeDocument("c.pdf", invoiceDate: "2026-01-15", subtotal: "2000000", vat: "160000", total: "2160000");

        var model = sut.Build(AccountingReportType.PurchaseInvoiceList, Header, From, To, [docA, docB, docC]);

        Assert.Empty(model.Skipped);
        Assert.Equal(2, model.Groups.Count);

        var group10 = Assert.Single(model.Groups, g => g.GroupLabel == "10%");
        Assert.Equal(2, group10.Rows.Count);
        Assert.Equal(15000000m, group10.Subtotal.Subtotal);
        Assert.Equal(1500000m, group10.Subtotal.Vat);
        Assert.Equal(16500000m, group10.Subtotal.Total);

        var group8 = Assert.Single(model.Groups, g => g.GroupLabel == "8%");
        Assert.Single(group8.Rows);
        Assert.Equal(2000000m, group8.Subtotal.Subtotal);

        Assert.Equal(17000000m, model.GrandTotal.Subtotal);
        Assert.Equal(1660000m, model.GrandTotal.Vat);
        Assert.Equal(18660000m, model.GrandTotal.Total);

        // Sequential numbering runs across groups (ordered 8% then 10%), not reset per group.
        var allRows = model.Groups.SelectMany(g => g.Rows).OrderBy(r => r.SequenceNumber).ToList();
        Assert.Equal([1, 2, 3], allRows.Select(r => r.SequenceNumber));
    }

    [Fact]
    public void Build_PurchaseInvoiceList_PopulatesCounterpartyFromSupplierFields()
    {
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());
        var doc = MakeDocument("a.pdf", invoiceDate: "2026-01-05", subtotal: "10000000", vat: "1000000", total: "11000000");

        var model = sut.Build(AccountingReportType.PurchaseInvoiceList, Header, From, To, [doc]);

        var row = model.Groups.Single().Rows.Single();
        Assert.Equal("NHA CUNG CAP XYZ", row.CounterpartyName);
        Assert.Equal("0309876543", row.CounterpartyTaxCode);
    }

    [Fact]
    public void Build_SalesInvoiceList_LeavesCounterpartyBlank()
    {
        // No "buyer" field exists in the extraction pipeline today — SupplierName/SupplierTaxCode
        // are always the seller, so showing them as "buyer" on a sales list would be wrong data.
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());
        var doc = MakeDocument("a.pdf", invoiceDate: "2026-01-05", subtotal: "10000000", vat: "1000000", total: "11000000");

        var model = sut.Build(AccountingReportType.SalesInvoiceList, Header, From, To, [doc]);

        var row = model.Groups.Single().Rows.Single();
        Assert.Null(row.CounterpartyName);
        Assert.Null(row.CounterpartyTaxCode);
    }

    [Fact]
    public void Build_NoDocumentsInPeriod_ReturnsEmptyGroupsAndZeroTotals()
    {
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());

        var model = sut.Build(AccountingReportType.PurchaseInvoiceList, Header, From, To, []);

        Assert.Empty(model.Groups);
        Assert.Empty(model.Skipped);
        Assert.Equal(AccountingReportTotals.Zero, model.GrandTotal);
    }

    [Fact]
    public void Build_RevenueLedger_NoDocumentsInPeriod_ReturnsSingleEmptyGroupAndZeroTotal()
    {
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());

        var model = sut.Build(AccountingReportType.RevenueLedger_S1HKD, Header, From, To, []);

        var group = Assert.Single(model.Groups);
        Assert.Null(group.GroupLabel);
        Assert.Empty(group.Rows);
        Assert.Equal(AccountingReportTotals.Zero, model.GrandTotal);
    }

    [Fact]
    public void Build_DocumentMissingInvoiceDate_IsSkippedNotIncludedInRows()
    {
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());
        var goodDoc = MakeDocument("good.pdf", invoiceDate: "2026-01-05", subtotal: "1000000", vat: "100000", total: "1100000");
        var badDoc = MakeDocument("bad.pdf", invoiceDate: null, subtotal: "1000000", vat: "100000", total: "1100000");

        var model = sut.Build(AccountingReportType.PurchaseInvoiceList, Header, From, To, [goodDoc, badDoc]);

        var skipped = Assert.Single(model.Skipped);
        Assert.Equal(badDoc.Id, skipped.DocumentId);
        Assert.Equal("bad.pdf", skipped.FileName);
        Assert.Contains("ngày hóa đơn", skipped.Reason, StringComparison.OrdinalIgnoreCase);

        var onlyRow = Assert.Single(model.Groups.Single().Rows);
        Assert.Equal(goodDoc.Id, onlyRow.DocumentId);
    }

    [Fact]
    public void Build_RevenueLedger_MissingInvoiceDate_IsSkippedAndDoesNotAffectTotal()
    {
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());
        var goodDoc = MakeDocument("good.pdf", invoiceDate: "2026-01-05", total: "1100000");
        var badDoc = MakeDocument("bad.pdf", invoiceDate: null, total: "9999999");

        var model = sut.Build(AccountingReportType.RevenueLedger_S1HKD, Header, From, To, [goodDoc, badDoc]);

        Assert.Single(model.Skipped);
        Assert.Equal(1100000m, model.GrandTotal.Total);
    }

    [Fact]
    public void Build_PurchaseInvoiceList_MissingSubtotal_IsSkippedWithVatRateReason()
    {
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());
        var doc = MakeDocument("bad.pdf", invoiceDate: "2026-01-05", subtotal: null, vat: "100000", total: "1100000");

        var model = sut.Build(AccountingReportType.PurchaseInvoiceList, Header, From, To, [doc]);

        var skipped = Assert.Single(model.Skipped);
        Assert.Contains("thuế suất", skipped.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(model.Groups);
    }

    [Fact]
    public void Build_RevenueLedger_UsesTotalAmountAsDoanhThu_SortedByInvoiceDate()
    {
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());
        var later = MakeDocument("later.pdf", invoiceDate: "2026-01-20", total: "5000000");
        var earlier = MakeDocument("earlier.pdf", invoiceDate: "2026-01-05", total: "3000000");

        var model = sut.Build(AccountingReportType.RevenueLedger_S1HKD, Header, From, To, [later, earlier]);

        var rows = model.Groups.Single().Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(earlier.Id, rows[0].DocumentId);
        Assert.Equal(1, rows[0].SequenceNumber);
        Assert.Equal(later.Id, rows[1].DocumentId);
        Assert.Equal(2, rows[1].SequenceNumber);
        Assert.Equal(8000000m, model.GrandTotal.Total);
    }

    [Fact]
    public void Build_DocumentOutsidePeriod_IsExcludedWithoutWarning()
    {
        var sut = new AccountingReportBuilder(new DocumentProfileCatalog());
        var outOfRange = MakeDocument("outside.pdf", invoiceDate: "2025-12-31", total: "1000000");

        var model = sut.Build(AccountingReportType.RevenueLedger_S1HKD, Header, From, To, [outOfRange]);

        Assert.Empty(model.Skipped);
        Assert.Empty(model.Groups.Single().Rows);
    }

    private static Document MakeDocument(
        string fileName,
        string? invoiceDate,
        string? subtotal = null,
        string? vat = null,
        string total = "0",
        string supplierName = "NHA CUNG CAP XYZ",
        string supplierTaxCode = "0309876543")
    {
        var document = new Document
        {
            OrganizationId = Guid.NewGuid(),
            OriginalFileName = fileName,
            StoredFilePath = $"2026/01/{fileName}",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Status = DocumentStatus.Reviewed,
            DocumentType = DocumentType.Invoice,
            UpdatedAt = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc)
        };

        void AddField(FieldName name, string? value)
        {
            if (value is null) return;
            document.Fields.Add(new ExtractedField
            {
                DocumentId = document.Id,
                FieldName = name.ToString(),
                RawValue = value,
                NormalizedValue = value
            });
        }

        AddField(FieldName.InvoiceDate, invoiceDate);
        AddField(FieldName.InvoiceNumber, "INV-0001");
        AddField(FieldName.SupplierName, supplierName);
        AddField(FieldName.SupplierTaxCode, supplierTaxCode);
        AddField(FieldName.SubtotalAmount, subtotal);
        AddField(FieldName.VatAmount, vat);
        AddField(FieldName.TotalAmount, total);

        return document;
    }
}
