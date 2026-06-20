using Cerdik.Domain.Common;

namespace Cerdik.Domain.Entities;

/// <summary>A single-use, time-limited password-reset token. Only the HMAC hash is stored at rest.</summary>
public class PasswordResetToken : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = default!;

    public string TokenHash { get; set; } = default!;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public string? RequestedFromIp { get; set; }

    public bool IsActive => UsedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}
