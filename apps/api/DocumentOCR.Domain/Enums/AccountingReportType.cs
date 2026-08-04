namespace DocumentOCR.Domain.Enums;

/// <summary>Accounting-service export formats aligned with Thông tư 88/2021/TT-BTC, distinct from the raw data-dump Excel export (<see cref="DocumentOCR.Application.Interfaces.IExcelExportService"/>).</summary>
public enum AccountingReportType
{
    /// <summary>Sổ chi tiết doanh thu — Mẫu S1-HKD.</summary>
    RevenueLedger_S1HKD = 0,

    /// <summary>Bảng kê hóa đơn, chứng từ hàng hóa, dịch vụ mua vào.</summary>
    PurchaseInvoiceList = 1,

    /// <summary>Bảng kê hóa đơn, chứng từ hàng hóa, dịch vụ bán ra.</summary>
    SalesInvoiceList = 2
}
