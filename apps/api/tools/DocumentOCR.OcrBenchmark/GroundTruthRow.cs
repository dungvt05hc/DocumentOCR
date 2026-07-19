namespace DocumentOCR.OcrBenchmark;

/// <summary>One row of <c>ground-truth.csv</c> — the expected field values for one sample file.</summary>
public sealed record GroundTruthRow(
    string FileName,
    string? DocumentCategory,
    string? DocumentSubType,
    string? ExpectedSupplierName,
    string? ExpectedSupplierTaxCode,
    string? ExpectedBuyerName,
    string? ExpectedBuyerTaxCode,
    string? ExpectedInvoiceNumber,
    string? ExpectedInvoiceDate,
    string? ExpectedSubtotalAmount,
    string? ExpectedVatAmount,
    string? ExpectedTotalAmount,
    string? ExpectedCurrency,
    string? QualityLevel,
    string? Notes);
