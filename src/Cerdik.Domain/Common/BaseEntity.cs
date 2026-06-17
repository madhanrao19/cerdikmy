namespace Cerdik.Domain.Common;

/// <summary>Base type for all persisted aggregates. Uses GUID v7-friendly keys.</summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Soft-delete / anonymization marker for privacy compliance.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>Marker for entities that belong to a tenant <see cref="Organization"/>.</summary>
public interface ITenantScoped
{
    Guid OrganizationId { get; set; }
}
