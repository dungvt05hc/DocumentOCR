namespace DocumentOCR.OcrBenchmark;

/// <summary>One row of the benchmark summary CSV — one (file, provider) combination.</summary>
public sealed record BenchmarkCsvRow(
    string FileName,
    string ProviderName,
    string? ModelId,
    double ProcessingDurationMs,
    int PageCount,
    int FullTextLength,
    double? AverageConfidence,
    string? SupplierTaxCode,
    string? InvoiceDate,
    string? TotalAmount,
    int WarningCount,
    string? ErrorMessage);
