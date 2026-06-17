using Cerdik.Application.Ai;
using Cerdik.Domain;
using Cerdik.Domain.Entities;

namespace Cerdik.Application.Abstractions;

/// <summary>S3-compatible (MinIO/AWS) and Azure Blob both implement this storage abstraction.</summary>
public interface IStorageService
{
    string Provider { get; }
    Task<string> PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream> GetAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Pre-signed URL for direct browser upload/download.</summary>
    Task<string> GetPresignedUrlAsync(string key, TimeSpan expiry, bool forUpload = false, CancellationToken ct = default);
}

/// <summary>Hashing & verification of passwords (BCrypt under the hood).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset AccessExpiresAt, DateTimeOffset RefreshExpiresAt);

/// <summary>Issues and validates JWT access tokens and opaque refresh tokens.</summary>
public interface ITokenService
{
    TokenPair Issue(User user);
    string HashRefreshToken(string rawToken);
}

/// <summary>Ambient accessor for the authenticated principal (populated from the JWT).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Guid? OrganizationId { get; }
    Guid? StudentId { get; }
    UserRole? Role { get; }
    string? Email { get; }
    bool IsInRole(params UserRole[] roles);
}

/// <summary>Retrieval over the embedded lesson corpus, filtered by curriculum context.</summary>
public interface IVectorRetriever
{
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default);
}

public sealed record RetrievalQuery
{
    public required string QueryText { get; init; }
    public required string CurriculumVersionCode { get; init; }
    public required Guid SubjectId { get; init; }
    public required SchoolType SchoolType { get; init; }
    public required Language Language { get; init; }
    public required DlpMode DlpMode { get; init; }
    public int TopK { get; init; } = 5;
    public double MinScore { get; init; } = 0.2;
}

/// <summary>Two-stage moderation around tutor generation, plus intervention raising.</summary>
public interface IModerationService
{
    Task<ModerationOutcome> ScreenAsync(string text, ModerationStage stage, CancellationToken ct = default);
}

public sealed record ModerationOutcome(ModerationDecision Decision, RiskLevel Risk, IReadOnlyList<string> Categories, bool RaiseIntervention, string? Reason)
{
    public bool Allowed => Decision is ModerationDecision.Allow or ModerationDecision.Flag;
}

/// <summary>Outbound email (SMTP in dev/on-prem).</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>Payment provider abstraction (Billplz / Curlec / Stripe).</summary>
public interface IPaymentProvider
{
    PaymentProvider Provider { get; }
    Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default);

    /// <summary>Verify a webhook signature and normalize the event.</summary>
    Task<PaymentWebhookEvent> HandleWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default);
}

public interface IPaymentProviderFactory
{
    IPaymentProvider Resolve(PaymentProvider provider);
}

public sealed record CheckoutRequest(Guid HouseholdId, string PlanCode, int AmountCents, string Currency, string CustomerEmail, string ReturnUrl);
public sealed record CheckoutSession(string ProviderSessionId, string CheckoutUrl);
public sealed record PaymentWebhookEvent(bool Verified, PaymentStatus Status, string ProviderPaymentId, string? ProviderSubscriptionId, int AmountCents, string Currency, string RawJson);

/// <summary>Abstracts wall-clock for testability.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
