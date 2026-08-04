using System.Globalization;
using DocumentOCR.Application.Interfaces;
using DocumentOCR.Application.Models;
using DocumentOCR.Domain.Entities;
using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Processing;

/// <summary>
/// Pure aggregation logic for the Thông tù 88/2021/TT-BTC accounting report exports — grouping by
/// VAT rate, per-group and grand-total sums, period-end sorting. No ClosedXML, no DB access; see
/// <see cref="IAccountingReportBuilder"/>.
/// </summary>
public sealed class AccountingReportBuilder : IAccountingReportBuilder
{
    /// <summary>
    /// Reverse alias lookup, same purpose and shape as <c>ClosedXmlExportService</c>'s: a document
    /// whose field was saved under a review-profile alias key (e.g. "SellerName") instead of the
    /// legacy canonical <see cref="FieldName"/> (e.g. "SupplierName") must still be found. Kept as
    /// a separate small copy here — deliberately not shared with the legacy export, which the brief
    /// requires to stay untouched.
    /// </summary>
    private readonly Dictionary<string, List<string>> _canonicalToAliasKeys;

    public AccountingReportBuilder(IDocumentProfileCatalog profileCatalog)
    {
        _canonicalToAliasKeys = BuildCanonicalToAliasKeys(profileCatalog);
    }

    private static Dictionary<string, List<string>> BuildCanonicalToAliasKeys(IDocumentProfileCatalog profileCatalog)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var profiles = Enum.GetValues<DocumentCategory>()
            .Select(profileCatalog.GetProfile)
            .Distinct();

        foreach (var field in profiles.SelectMany(p => p.Sections).SelectMany(s => s.Fields))
        {
            foreach (var canonicalKey in field.AliasFieldNames)
            {
                if (!map.TryGetValue(canonicalKey, out var aliasKeys))
                {
                    aliasKeys = [];
                    map[canonicalKey] = aliasKeys;
                }

                aliasKeys.Add(field.FieldKey);
            }
        }

        return map;
    }

    private string? ResolveFieldValue(IReadOnlyDictionary<string, string?> fields, string canonicalKey)
    {
        if (fields.TryGetValue(canonicalKey, out var direct) && !string.IsNullOrWhiteSpace(direct))
            return direct;

        if (!_canonicalToAliasKeys.TryGetValue(canonicalKey, out var aliasKeys))
            return null;

        foreach (var aliasKey in aliasKeys)
        {
            if (fields.TryGetValue(aliasKey, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    public AccountingReportModel Build(
        AccountingReportType type,
        AccountingReportHeader header,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<Document> documents)
    {
        var rows = new List<AccountingReportRow>();
        var skipped = new List<AccountingReportSkippedDocument>();

        foreach (var document in documents.OrderBy(d => d.CreatedAt))
        {
            var fields = document.Fields.ToDictionary(f => f.FieldName, f => f.NormalizedValue, StringComparer.Ordinal);

            var invoiceDate = ParseDate(ResolveFieldValue(fields, nameof(FieldName.InvoiceDate)));
            var total = ParseMoney(ResolveFieldValue(fields, nameof(FieldName.TotalAmount)));

            if (invoiceDate is null)
            {
                skipped.Add(new AccountingReportSkippedDocument(document.Id, document.OriginalFileName, "Thiếu hoặc không đọc được ngày hóa đơn."));
                continue;
            }

            // Outside the requested period — a normal exclusion, not a data-quality problem, so no
            // warning entry (the caller is expected to have already scoped the query to the client,
            // but date parsing/range-checking only happens here where NormalizedValue is read).
            if (invoiceDate.Value < from || invoiceDate.Value > to)
                continue;

            if (total is null)
            {
                skipped.Add(new AccountingReportSkippedDocument(document.Id, document.OriginalFileName, "Thiếu tổng thanh toán."));
                continue;
            }

            var subtotal = ParseMoney(ResolveFieldValue(fields, nameof(FieldName.SubtotalAmount)));
            var vat = ParseMoney(ResolveFieldValue(fields, nameof(FieldName.VatAmount)));

            decimal? vatRate = null;
            if (type != AccountingReportType.RevenueLedger_S1HKD)
            {
                // Thuế suất isn't an extracted field — it's derived from the two amounts that are.
                // Without a positive subtotal to divide by, the rate (and therefore which group this
                // row belongs in) can't be determined, so the row is surfaced as a warning instead
                // of guessed into a group.
                if (subtotal is null or <= 0m || vat is null)
                {
                    skipped.Add(new AccountingReportSkippedDocument(
                        document.Id, document.OriginalFileName,
                        "Không xác định được thuế suất (thiếu giá trị chưa thuế hoặc tiền thuế)."));
                    continue;
                }

                vatRate = Math.Round(vat.Value / subtotal.Value * 100m, 0, MidpointRounding.AwayFromZero);
            }

            var supplierName = ResolveFieldValue(fields, nameof(FieldName.SupplierName));
            var supplierTaxCode = ResolveFieldValue(fields, nameof(FieldName.SupplierTaxCode));
            var notes = ResolveFieldValue(fields, nameof(FieldName.Notes));
            var invoiceNumber = ResolveFieldValue(fields, nameof(FieldName.InvoiceNumber));

            // The counterparty on a purchase invoice is the seller — exactly what SupplierName/
            // SupplierTaxCode extract. On a sales invoice the counterparty would be the buyer, which
            // nothing in the extraction pipeline captures today; left null rather than mislabeling
            // the seller as the buyer.
            var (counterpartyName, counterpartyTaxCode) = type == AccountingReportType.PurchaseInvoiceList
                ? (supplierName, supplierTaxCode)
                : (null, null);

            rows.Add(new AccountingReportRow(
                SequenceNumber: 0,
                BookingDate: DateOnly.FromDateTime(document.UpdatedAt),
                DocumentNumber: invoiceNumber,
                DocumentDate: invoiceDate,
                Description: !string.IsNullOrWhiteSpace(notes) ? notes : supplierName,
                CounterpartyName: counterpartyName,
                CounterpartyTaxCode: counterpartyTaxCode,
                SubtotalAmount: subtotal,
                VatRatePercent: vatRate,
                VatAmount: vat,
                TotalAmount: total,
                DocumentId: document.Id));
        }

        IReadOnlyList<AccountingReportGroup> groups;
        AccountingReportTotals grandTotal;

        if (type == AccountingReportType.RevenueLedger_S1HKD)
        {
            var sorted = Renumber(rows.OrderBy(r => r.DocumentDate).ThenBy(r => r.DocumentNumber).ToList());
            var subtotal = Sum(sorted);
            groups = [new AccountingReportGroup(null, sorted, subtotal)];
            grandTotal = subtotal;
        }
        else
        {
            var groupList = new List<AccountingReportGroup>();
            var sequence = 1;

            foreach (var rateGroup in rows.GroupBy(r => r.VatRatePercent!.Value).OrderBy(g => g.Key))
            {
                var groupRows = new List<AccountingReportRow>();
                foreach (var row in rateGroup.OrderBy(r => r.DocumentDate).ThenBy(r => r.DocumentNumber))
                {
                    groupRows.Add(row with { SequenceNumber = sequence++ });
                }

                groupList.Add(new AccountingReportGroup(
                    $"{rateGroup.Key.ToString("0", CultureInfo.InvariantCulture)}%",
                    groupRows,
                    Sum(groupRows)));
            }

            groups = groupList;
            grandTotal = Sum(groups.SelectMany(g => g.Rows).ToList());
        }

        return new AccountingReportModel(type, header, from, to, groups, grandTotal, skipped);
    }

    private static List<AccountingReportRow> Renumber(List<AccountingReportRow> rows)
    {
        var result = new List<AccountingReportRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
            result.Add(rows[i] with { SequenceNumber = i + 1 });
        return result;
    }

    private static AccountingReportTotals Sum(IReadOnlyList<AccountingReportRow> rows) =>
        rows.Count == 0
            ? AccountingReportTotals.Zero
            : new AccountingReportTotals(
                rows.Sum(r => r.SubtotalAmount ?? 0m),
                rows.Sum(r => r.VatAmount ?? 0m),
                rows.Sum(r => r.TotalAmount ?? 0m));

    private static DateOnly? ParseDate(string? isoValue)
    {
        return DateOnly.TryParseExact(isoValue, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static decimal? ParseMoney(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }
}
