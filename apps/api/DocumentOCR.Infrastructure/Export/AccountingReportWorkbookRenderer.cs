using ClosedXML.Excel;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Infrastructure.Export;

/// <summary>
/// Draws an already-computed <see cref="AccountingReportModel"/> onto an .xlsx workbook via
/// ClosedXML. Contains no aggregation logic (grouping/subtotals/sorting all happened in
/// <see cref="IAccountingReportBuilder"/>) — this only lays the final model out on sheets, mirroring
/// the "interface in Application, ClosedXML in Infrastructure" split <c>ClosedXmlExportService</c>
/// already uses for the legacy export.
/// </summary>
public class AccountingReportWorkbookRenderer : IAccountingReportWorkbookRenderer
{
    private const string FontName = "Times New Roman";
    private const double FontSize = 12;
    private const string DateFormat = "dd/MM/yyyy";
    private const string MoneyFormat = "#,##0";

    public byte[] Render(AccountingReportModel model)
    {
        using var workbook = new XLWorkbook();

        switch (model.Type)
        {
            case AccountingReportType.RevenueLedger_S1HKD:
                BuildRevenueLedgerSheet(workbook, model);
                break;
            case AccountingReportType.PurchaseInvoiceList:
                BuildInvoiceListSheet(workbook, model, "BangKeMuaVao",
                    "BẢNG KÊ HÓA ĐƠN, CHỨNG TỪ HÀNG HÓA, DỊCH VỤ MUA VÀO",
                    "Tên người bán", "MST người bán");
                break;
            case AccountingReportType.SalesInvoiceList:
                BuildInvoiceListSheet(workbook, model, "BangKeBanRa",
                    "BẢNG KÊ HÓA ĐƠN, CHỨNG TỪ HÀNG HÓA, DỊCH VỤ BÁN RA",
                    "Tên người mua", "MST người mua");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(model), model.Type, "Unknown accounting report type.");
        }

        if (model.Skipped.Count > 0)
            BuildWarningsSheet(workbook, model.Skipped);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private void BuildRevenueLedgerSheet(XLWorkbook workbook, AccountingReportModel model)
    {
        const int columnCount = 7;
        var worksheet = workbook.Worksheets.Add("SoChiTietDoanhThu");
        ApplyDefaultFont(worksheet);

        var row = WriteTitleBlock(worksheet, model, columnCount, "SỔ CHI TIẾT DOANH THU (Mẫu S1-HKD)");

        string[] headers = ["STT", "Ngày tháng ghi sổ", "Số hiệu chứng từ", "Ngày chứng từ", "Diễn giải", "Doanh thu", "Ghi chú"];
        var headerRow = row;
        WriteColumnHeaders(worksheet, headerRow, headers);
        row++;

        var rows = model.Groups.Count > 0 ? model.Groups[0].Rows : [];
        foreach (var reportRow in rows)
        {
            worksheet.Cell(row, 1).Value = reportRow.SequenceNumber;
            SetDateCell(worksheet.Cell(row, 2), reportRow.BookingDate);
            worksheet.Cell(row, 3).Value = reportRow.DocumentNumber ?? string.Empty;
            SetDateCell(worksheet.Cell(row, 4), reportRow.DocumentDate);
            worksheet.Cell(row, 5).Value = reportRow.Description ?? string.Empty;
            SetMoneyCell(worksheet.Cell(row, 6), reportRow.TotalAmount);
            worksheet.Cell(row, 7).Value = string.Empty;
            row++;
        }

        var lastDataRow = row - 1;

        worksheet.Range(row, 1, row, 5).Merge().Value = "Cộng phát sinh:";
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        SetMoneyCell(worksheet.Cell(row, 6), model.GrandTotal.Total);
        worksheet.Cell(row, 6).Style.Font.Bold = true;

        ApplyTableBorder(worksheet, headerRow, row, columnCount);
        FinishSheet(worksheet, headerRow, columnCount, lastDataRow: row);
    }

    private void BuildInvoiceListSheet(
        XLWorkbook workbook,
        AccountingReportModel model,
        string sheetName,
        string title,
        string counterpartyNameLabel,
        string counterpartyTaxCodeLabel)
    {
        const int columnCount = 9;
        var worksheet = workbook.Worksheets.Add(sheetName);
        ApplyDefaultFont(worksheet);

        var row = WriteTitleBlock(worksheet, model, columnCount, title);

        string[] headers =
        [
            "STT", "Số hóa đơn", "Ngày", counterpartyNameLabel, counterpartyTaxCodeLabel,
            "Giá trị chưa thuế", "Thuế suất", "Tiền thuế", "Tổng thanh toán"
        ];
        var headerRow = row;
        WriteColumnHeaders(worksheet, headerRow, headers);
        row++;

        foreach (var group in model.Groups)
        {
            foreach (var reportRow in group.Rows)
            {
                worksheet.Cell(row, 1).Value = reportRow.SequenceNumber;
                worksheet.Cell(row, 2).Value = reportRow.DocumentNumber ?? string.Empty;
                SetDateCell(worksheet.Cell(row, 3), reportRow.DocumentDate);
                worksheet.Cell(row, 4).Value = reportRow.CounterpartyName ?? string.Empty;
                worksheet.Cell(row, 5).Value = reportRow.CounterpartyTaxCode ?? string.Empty;
                SetMoneyCell(worksheet.Cell(row, 6), reportRow.SubtotalAmount);
                worksheet.Cell(row, 7).Value = reportRow.VatRatePercent.HasValue ? $"{reportRow.VatRatePercent.Value:0}%" : string.Empty;
                SetMoneyCell(worksheet.Cell(row, 8), reportRow.VatAmount);
                SetMoneyCell(worksheet.Cell(row, 9), reportRow.TotalAmount);
                row++;
            }

            worksheet.Range(row, 1, row, 5).Merge().Value = $"Cộng {group.GroupLabel}:";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            SetMoneyCell(worksheet.Cell(row, 6), group.Subtotal.Subtotal);
            SetMoneyCell(worksheet.Cell(row, 8), group.Subtotal.Vat);
            SetMoneyCell(worksheet.Cell(row, 9), group.Subtotal.Total);
            worksheet.Range(row, 6, row, 9).Style.Font.Bold = true;
            row++;
        }

        worksheet.Range(row, 1, row, 5).Merge().Value = "TỔNG CỘNG";
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        SetMoneyCell(worksheet.Cell(row, 6), model.GrandTotal.Subtotal);
        SetMoneyCell(worksheet.Cell(row, 8), model.GrandTotal.Vat);
        SetMoneyCell(worksheet.Cell(row, 9), model.GrandTotal.Total);
        worksheet.Range(row, 6, row, 9).Style.Font.Bold = true;

        ApplyTableBorder(worksheet, headerRow, row, columnCount);
        FinishSheet(worksheet, headerRow, columnCount, lastDataRow: row);
    }

    private static void BuildWarningsSheet(XLWorkbook workbook, IReadOnlyList<AccountingReportSkippedDocument> skipped)
    {
        var worksheet = workbook.Worksheets.Add("CanhBao");
        ApplyDefaultFont(worksheet);

        worksheet.Cell(1, 1).Value = "Tên tệp";
        worksheet.Cell(1, 2).Value = "Lý do không đưa vào báo cáo";
        worksheet.Range(1, 1, 1, 2).Style.Font.Bold = true;

        var row = 2;
        foreach (var item in skipped)
        {
            worksheet.Cell(row, 1).Value = item.FileName;
            worksheet.Cell(row, 2).Value = item.Reason;
            row++;
        }

        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns(1, 2).AdjustToContents();
    }

    /// <summary>Writes the title + client-identity block common to every report type and returns the next free row (where the column-header row should go).</summary>
    private static int WriteTitleBlock(IXLWorksheet worksheet, AccountingReportModel model, int columnCount, string title)
    {
        worksheet.Range(1, 1, 1, columnCount).Merge().Value = title;
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = FontSize + 2;
        worksheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var clientLabel = model.Header.ClientType == ClientType.Enterprise ? "Tên doanh nghiệp" : "Tên hộ kinh doanh";
        worksheet.Range(2, 1, 2, columnCount).Merge().Value = $"{clientLabel}: {model.Header.ClientName}";

        var midColumn = Math.Max(2, columnCount / 2);
        worksheet.Range(3, 1, 3, midColumn).Merge().Value = $"Địa chỉ: {model.Header.Address ?? string.Empty}";
        worksheet.Range(3, midColumn + 1, 3, columnCount).Merge().Value = $"MST: {model.Header.TaxCode ?? string.Empty}";

        worksheet.Range(4, 1, 4, columnCount).Merge().Value = $"Kỳ báo cáo: {model.From:dd/MM/yyyy} - {model.To:dd/MM/yyyy}";

        return 6;
    }

    private static void WriteColumnHeaders(IXLWorksheet worksheet, int row, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++)
            worksheet.Cell(row, i + 1).Value = headers[i];

        var header = worksheet.Range(row, 1, row, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F5F8B");
        header.Style.Font.FontColor = XLColor.White;
    }

    private static void ApplyDefaultFont(IXLWorksheet worksheet)
    {
        worksheet.Style.Font.FontName = FontName;
        worksheet.Style.Font.FontSize = FontSize;
    }

    private static void ApplyTableBorder(IXLWorksheet worksheet, int headerRow, int lastRow, int columnCount)
    {
        var range = worksheet.Range(headerRow, 1, lastRow, columnCount);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
    }

    private static void FinishSheet(IXLWorksheet worksheet, int headerRow, int columnCount, int lastDataRow)
    {
        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.Columns(1, columnCount).AdjustToContents();

        worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.PrintAreas.Add(1, 1, lastDataRow, columnCount);
    }

    private static void SetDateCell(IXLCell cell, DateOnly? value)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value.ToDateTime(TimeOnly.MinValue);
            cell.Style.DateFormat.Format = DateFormat;
        }
        else
        {
            cell.Value = string.Empty;
        }
    }

    private static void SetMoneyCell(IXLCell cell, decimal? value)
    {
        if (value.HasValue)
        {
            cell.Value = value.Value;
            cell.Style.NumberFormat.Format = MoneyFormat;
        }
        else
        {
            cell.Value = string.Empty;
        }
    }
}
