using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Interfaces;

/// <summary>
/// Pure aggregation logic (grouping by VAT rate, subtotals, period-end totals, sorting) for the
/// Thông tư 88/2021/TT-BTC accounting report exports. Takes already-loaded <see cref="Document"/>
/// entities (with <c>Fields</c> populated) and produces a fully-computed <see cref="AccountingReportModel"/>
/// — no ClosedXML, no DB access, no infrastructure dependency, so it is testable in isolation.
/// </summary>
public interface IAccountingReportBuilder
{
    AccountingReportModel Build(
        AccountingReportType type,
        AccountingReportHeader header,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<Document> documents);
}
