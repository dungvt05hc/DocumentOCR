using ClosedXML.Excel;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Enums;
using DocumentOCR.Infrastructure.Export;
using Xunit;

namespace DocumentOCR.UnitTests.Export;

public class AccountingReportWorkbookRendererTests
{
    private static readonly AccountingReportHeader Header = new("CONG TY TNHH ABC", "123 Le Loi, Q1", "0100109106", ClientType.HouseholdBusiness);
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 1, 31);

    [Fact]
    public void Render_RevenueLedger_WritesLedgerSheetWithDataAndFooterRow()
    {
        var row = new AccountingReportRow(1, new DateOnly(2026, 1, 25), "INV-0001", new DateOnly(2026, 1, 5), "Ban hang", null, null, null, null, null, 11000000m, Guid.NewGuid());
        var group = new AccountingReportGroup(null, [row], new AccountingReportTotals(0, 0, 11000000m));
        var model = new AccountingReportModel(AccountingReportType.RevenueLedger_S1HKD, Header, From, To, [group], new AccountingReportTotals(0, 0, 11000000m), []);

        var sut = new AccountingReportWorkbookRenderer();
        using var workbook = new XLWorkbook(new MemoryStream(sut.Render(model)));

        Assert.Contains(workbook.Worksheets, s => s.Name == "SoChiTietDoanhThu");
        var sheet = workbook.Worksheet("SoChiTietDoanhThu");

        Assert.Equal("STT", sheet.Cell(6, 1).GetString());
        Assert.Equal("Doanh thu", sheet.Cell(6, 6).GetString());

        Assert.Equal(1, sheet.Cell(7, 1).GetValue<int>());
        Assert.Equal("INV-0001", sheet.Cell(7, 3).GetString());
        Assert.Equal(11000000m, sheet.Cell(7, 6).GetValue<decimal>());

        Assert.Equal("Cộng phát sinh:", sheet.Cell(8, 1).GetString());
        Assert.Equal(11000000m, sheet.Cell(8, 6).GetValue<decimal>());

        Assert.DoesNotContain(workbook.Worksheets, s => s.Name == "CanhBao");
    }

    [Fact]
    public void Render_PurchaseInvoiceList_GroupsByRateWithSubtotalsAndGrandTotal()
    {
        var row10a = MakeRow(1, "AA-1", 10_000_000m, 10m, 1_000_000m, 11_000_000m);
        var row10b = MakeRow(2, "AA-2", 5_000_000m, 10m, 500_000m, 5_500_000m);
        var row8 = MakeRow(3, "AA-3", 2_000_000m, 8m, 160_000m, 2_160_000m);

        var group10 = new AccountingReportGroup("10%", [row10a, row10b], new AccountingReportTotals(15_000_000m, 1_500_000m, 16_500_000m));
        var group8 = new AccountingReportGroup("8%", [row8], new AccountingReportTotals(2_000_000m, 160_000m, 2_160_000m));
        var grandTotal = new AccountingReportTotals(17_000_000m, 1_660_000m, 18_660_000m);

        var model = new AccountingReportModel(AccountingReportType.PurchaseInvoiceList, Header, From, To, [group8, group10], grandTotal, []);

        var sut = new AccountingReportWorkbookRenderer();
        using var workbook = new XLWorkbook(new MemoryStream(sut.Render(model)));

        var sheet = workbook.Worksheet("BangKeMuaVao");
        Assert.Equal("Tên người bán", sheet.Cell(6, 4).GetString());
        Assert.Equal("MST người bán", sheet.Cell(6, 5).GetString());

        // group8 rows first (row 7), its subtotal row (row 8), then group10 rows (9-10), its subtotal (11), then grand total (12)
        Assert.Equal("AA-3", sheet.Cell(7, 2).GetString());
        Assert.Equal("Cộng 8%:", sheet.Cell(8, 1).GetString());
        Assert.Equal(2_000_000m, sheet.Cell(8, 6).GetValue<decimal>());

        Assert.Equal("AA-1", sheet.Cell(9, 2).GetString());
        Assert.Equal("AA-2", sheet.Cell(10, 2).GetString());
        Assert.Equal("Cộng 10%:", sheet.Cell(11, 1).GetString());
        Assert.Equal(16_500_000m, sheet.Cell(11, 9).GetValue<decimal>());

        Assert.Equal("TỔNG CỘNG", sheet.Cell(12, 1).GetString());
        Assert.Equal(18_660_000m, sheet.Cell(12, 9).GetValue<decimal>());
    }

    [Fact]
    public void Render_SalesInvoiceList_UsesBuyerHeaderLabelsAndBlankCounterpartyCells()
    {
        var row = new AccountingReportRow(1, null, "AA-1", new DateOnly(2026, 1, 5), null, null, null, 10_000_000m, 10m, 1_000_000m, 11_000_000m, Guid.NewGuid());
        var group = new AccountingReportGroup("10%", [row], new AccountingReportTotals(10_000_000m, 1_000_000m, 11_000_000m));
        var model = new AccountingReportModel(AccountingReportType.SalesInvoiceList, Header, From, To, [group], new AccountingReportTotals(10_000_000m, 1_000_000m, 11_000_000m), []);

        var sut = new AccountingReportWorkbookRenderer();
        using var workbook = new XLWorkbook(new MemoryStream(sut.Render(model)));

        Assert.Contains(workbook.Worksheets, s => s.Name == "BangKeBanRa");
        var sheet = workbook.Worksheet("BangKeBanRa");
        Assert.Equal("Tên người mua", sheet.Cell(6, 4).GetString());
        Assert.Equal("MST người mua", sheet.Cell(6, 5).GetString());
        Assert.Equal(string.Empty, sheet.Cell(7, 4).GetString());
        Assert.Equal(string.Empty, sheet.Cell(7, 5).GetString());
    }

    [Fact]
    public void Render_WithSkippedDocuments_WritesWarningsSheet()
    {
        var model = new AccountingReportModel(
            AccountingReportType.PurchaseInvoiceList, Header, From, To, [], AccountingReportTotals.Zero,
            [new AccountingReportSkippedDocument(Guid.NewGuid(), "bad.pdf", "Thiếu hoặc không đọc được ngày hóa đơn.")]);

        var sut = new AccountingReportWorkbookRenderer();
        using var workbook = new XLWorkbook(new MemoryStream(sut.Render(model)));

        Assert.Contains(workbook.Worksheets, s => s.Name == "CanhBao");
        var sheet = workbook.Worksheet("CanhBao");
        Assert.Equal("bad.pdf", sheet.Cell(2, 1).GetString());
        Assert.Equal("Thiếu hoặc không đọc được ngày hóa đơn.", sheet.Cell(2, 2).GetString());
    }

    [Fact]
    public void Render_NoDocumentsInPeriod_StillProducesValidWorkbookWithZeroFooter()
    {
        var model = new AccountingReportModel(AccountingReportType.PurchaseInvoiceList, Header, From, To, [], AccountingReportTotals.Zero, []);

        var sut = new AccountingReportWorkbookRenderer();
        using var workbook = new XLWorkbook(new MemoryStream(sut.Render(model)));

        var sheet = workbook.Worksheet("BangKeMuaVao");
        Assert.Equal("TỔNG CỘNG", sheet.Cell(7, 1).GetString());
        Assert.Equal(0m, sheet.Cell(7, 9).GetValue<decimal>());
    }

    private static AccountingReportRow MakeRow(int seq, string invoiceNumber, decimal subtotal, decimal rate, decimal vat, decimal total) =>
        new(seq, null, invoiceNumber, new DateOnly(2026, 1, 5), null, "NHA CUNG CAP XYZ", "0309876543", subtotal, rate, vat, total, Guid.NewGuid());
}
