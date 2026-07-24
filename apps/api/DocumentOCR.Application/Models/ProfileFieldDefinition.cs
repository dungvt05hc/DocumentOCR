using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Models;

/// <summary>
/// Defines one field slot within a <see cref="ProfileSection"/> of a <see cref="DocumentProfile"/>.
/// Field data itself always lives on <c>ExtractedField</c> (keyed by its own <c>FieldName</c>) —
/// this only describes how that data should be labeled, typed, and required for a given
/// document category, and which legacy/alternate <c>ExtractedField.FieldName</c> values satisfy it.
/// </summary>
public sealed record ProfileFieldDefinition
{
    /// <summary>Canonical key used in the review response and by the save API (e.g. "SellerName").</summary>
    public required string FieldKey { get; init; }

    public required string Label { get; init; }

    public ReviewFieldDataType DataType { get; init; } = ReviewFieldDataType.Text;

    public bool IsRequired { get; init; }

    public required int DisplayOrder { get; init; }

    /// <summary>Severity used for the "missing" warning when this field is required and absent.</summary>
    public ValidationSeverity MissingSeverity { get; init; } = ValidationSeverity.High;

    /// <summary>
    /// Other <c>ExtractedField.FieldName</c> values that satisfy this slot (e.g. SellerName's
    /// alias is the legacy "SupplierName" the extractor actually produces today). Checked in
    /// order after <see cref="FieldKey"/> itself.
    /// </summary>
    public IReadOnlyList<string> AliasFieldNames { get; init; } = [];

    /// <summary>Fixed choices for Enum/Currency fields. Null means "render as free text".</summary>
    public IReadOnlyList<string>? Options { get; init; }
}
