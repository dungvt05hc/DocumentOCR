using DocumentOCR.Domain.Enums;

namespace DocumentOCR.Application.Models;

/// <summary>
/// Defines the review sections/fields shown to the user for one <see cref="DocumentCategory"/>.
/// Profiles are static, in-code data for this iteration (no DB/config-driven catalog yet) —
/// see <c>IDocumentProfileCatalog</c>.
/// </summary>
public sealed record DocumentProfile
{
    public required DocumentCategory Category { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<ProfileSection> Sections { get; init; }

    /// <summary>
    /// Flags categories whose extraction reliability is inherently lower (e.g. handwritten/mixed
    /// restaurant bills) — surfaced to the user as an informational warning rather than a new
    /// response field.
    /// </summary>
    public bool IsExperimental { get; init; }
}
