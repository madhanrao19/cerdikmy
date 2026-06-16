using Cerdik.Domain.Common;

namespace Cerdik.Domain.Entities;

/// <summary>Append-only audit log for security/compliance-relevant actions.</summary>
public class AuditLog : BaseEntity
{
    public Guid? OrganizationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorEmail { get; set; }

    public string Action { get; set; } = default!;
    public string EntityType { get; set; } = default!;
    public string? EntityId { get; set; }

    public string? Ip { get; set; }
    public string? UserAgent { get; set; }

    /// <summary>JSON diff/metadata. Never contains secrets or full PII payloads.</summary>
    public string? MetadataJson { get; set; }
}

/// <summary>A PDPA data-subject request (export or delete/anonymize).</summary>
public class PrivacyRequest : BaseEntity
{
    public Guid RequestedByUserId { get; set; }
    public Guid? StudentId { get; set; }

    public PrivacyRequestType Type { get; set; }
    public PrivacyRequestStatus Status { get; set; } = PrivacyRequestStatus.Received;
    public string? Reason { get; set; }

    /// <summary>Storage key of the produced export bundle (Export requests).</summary>
    public string? ResultStorageKey { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
