using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Models;

/// <summary>Client identity snapshot printed in the report header — captured at export time so a later rename/deactivation of the <c>ClientProfile</c> doesn't change a previously generated report.</summary>
public sealed record AccountingReportHeader(
    string ClientName,
    string? Address,
    string? TaxCode,
    ClientType ClientType);

/// <summary>
/// One row of a report. Not every column applies to every <see cref="AccountingReportType"/> — the
/// renderer picks which columns to draw per type. <see cref="CounterpartyName"/>/<see cref="CounterpartyTaxCode"/>
/// are populated only for <see cref="AccountingReportType.PurchaseInvoiceList"/> (the counterparty
/// there is the seller, which is exactly what <c>SupplierName</c>/<c>SupplierTaxCode</c> extract);
/// for <see cref="AccountingReportType.SalesInvoiceList"/> the counterparty would be the *buyer*,
/// which nothing in the extraction pipeline captures today, so they are deliberately left null
/// rather than mislabeling the seller as the buyer.
/// </summary>
public sealed record AccountingReportRow(
    int SequenceNumber,
    DateOnly? BookingDate,
    string? DocumentNumber,
    DateOnly? DocumentDate,
    string? Description,
    string? CounterpartyName,
    string? CounterpartyTaxCode,
    decimal? SubtotalAmount,
    decimal? VatRatePercent,
    decimal? VatAmount,
    decimal? TotalAmount,
    Guid DocumentId);

public sealed record AccountingReportTotals(decimal Subtotal, decimal Vat, decimal Total)
{
    public static readonly AccountingReportTotals Zero = new(0m, 0m, 0m);
}

/// <summary>A group of rows sharing a VAT rate (<see cref="GroupLabel"/> e.g. "10%"), or the single ungrouped set of rows for <see cref="AccountingReportType.RevenueLedger_S1HKD"/> (<see cref="GroupLabel"/> is null there).</summary>
public sealed record AccountingReportGroup(
    string? GroupLabel,
    IReadOnlyList<AccountingReportRow> Rows,
    AccountingReportTotals Subtotal);

/// <summary>A document in range that could not be placed on the report — surfaced in a warning sheet instead of being silently dropped or corrupting a group's totals.</summary>
public sealed record AccountingReportSkippedDocument(
    Guid DocumentId,
    string FileName,
    string Reason);

public sealed record AccountingReportModel(
    AccountingReportType Type,
    AccountingReportHeader Header,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<AccountingReportGroup> Groups,
    AccountingReportTotals GrandTotal,
    IReadOnlyList<AccountingReportSkippedDocument> Skipped);
