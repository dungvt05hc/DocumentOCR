using DocumentOCR.Domain.Common;

namespace DocumentOCR.Domain.Entities;

/// <summary>
/// One VAT-rate line of a Vietnamese invoice's tax breakdown (TT78 "THTTLTSuat/LTSuat") — an
/// invoice can carry multiple lines when it mixes goods/services taxed at different rates.
/// </summary>
public class InvoiceTaxBreakdown : BaseEntity
{
    public Guid DocumentId { get; set; }

    /// <summary>The rate text exactly as read from the source (XML tag or OCR text), kept for audit/cross-check.</summary>
    public string? RawVatRate { get; set; }

    /// <summary>Canonical rate: "0%", "5%", "8%", "10%", "KCT" (không chịu thuế), or "KKKNT" (không kê khai nộp thuế). Null if unrecognized.</summary>
    public string? VatRate { get; set; }

    public decimal? TaxableAmount { get; set; }
    public decimal? TaxAmount { get; set; }

    /// <summary>1.0 for a structured-XML-sourced row; the OCR candidate's own confidence for an OCR-inferred row.</summary>
    public double? Confidence { get; set; }

    public int SortOrder { get; set; }

    public Document Document { get; set; } = null!;
}
