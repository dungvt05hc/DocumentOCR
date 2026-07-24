namespace DocumentOCR.Application.Models;

/// <summary>A labeled group of <see cref="ProfileFieldDefinition"/>s within a <see cref="DocumentProfile"/>.</summary>
public sealed record ProfileSection
{
    public required string SectionKey { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public required int DisplayOrder { get; init; }

    public required IReadOnlyList<ProfileFieldDefinition> Fields { get; init; }
}
