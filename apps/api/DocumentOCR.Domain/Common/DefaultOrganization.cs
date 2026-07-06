namespace DocumentOCR.Domain.Common;

/// <summary>
/// The MVP is single-tenant: every request is scoped to this one seeded organization
/// instead of an authenticated user's claims. Replace with real multi-tenant org
/// resolution once auth lands.
/// </summary>
public static class DefaultOrganization
{
    public static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public const string Name = "Default Organization";
    public const string Slug = "default";

    /// <summary>Fixed seed timestamp so the EF Core migration snapshot stays stable.</summary>
    public static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
}
