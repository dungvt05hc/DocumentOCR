using DocumentOCR.Domain.Entities;

namespace DocumentOCR.Application.Models;

/// <summary>Result of parsing a structured invoice file (see <see cref="Interfaces.IStructuredInvoiceParser"/>).</summary>
public sealed class StructuredInvoiceResult
{
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Fields parsed with full confidence. <see cref="ExtractedField.DocumentId"/> is not set here.</summary>
    public IReadOnlyList<ExtractedField> Fields { get; init; } = [];

    /// <summary>The original file content, kept for artifact/audit persistence.</summary>
    public string? RawXml { get; init; }

    /// <summary>Invoice template code (e.g. TT78 "KHMSHDon"), if present.</summary>
    public string? InvoiceTemplateCode { get; init; }

    /// <summary>Invoice serial (e.g. TT78 "KHHDon"), if present.</summary>
    public string? InvoiceSerial { get; init; }
}
