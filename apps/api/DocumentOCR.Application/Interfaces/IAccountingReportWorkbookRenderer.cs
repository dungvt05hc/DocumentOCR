using DocumentOCR.Application.Models;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Draws an already-computed <see cref="AccountingReportModel"/> into an .xlsx workbook. Contains
/// no aggregation logic — the model handed in is final; this only lays it out on sheets. Implemented
/// in Infrastructure (ClosedXML), matching <see cref="IExcelExportService"/>'s "interface in
/// Application, ClosedXML in Infrastructure" split.
/// </summary>
public interface IAccountingReportWorkbookRenderer
{
    byte[] Render(AccountingReportModel model);
}
