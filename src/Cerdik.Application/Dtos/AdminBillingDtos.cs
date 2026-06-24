using Cerdik.Domain;

namespace Cerdik.Application.Dtos;

// ---- Admin: users ----
public sealed record AdminUserDto(Guid Id, string Email, string? FullName, UserRole Role, bool IsActive, Guid? HouseholdId, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);
public sealed record CreateAdminUserRequest(string Email, string Password, string FullName, UserRole Role);

// ---- Admin: content ----
public sealed record AdminContentItemDto(Guid LessonId, string Title, string SubjectName, SchoolType SchoolType, Language Language, DlpMode DlpMode, PublishState State, int BlockCount, int ActivityCount, DateTimeOffset UpdatedAt);
public sealed record ImportContentRequest(Guid SubjectVariantId, string Title, string Summary, string? LearningStandardCode, IReadOnlyList<ImportBlock> Blocks);
public sealed record ImportBlock(LessonBlockType Type, string? Markdown, string? ConfigJson);
public sealed record PublishContentRequest(Guid LessonId, bool Publish);

// ---- Admin: moderation ----
public sealed record ModerationQueueItemDto(
    Guid EventId,
    Guid TutorSessionId,
    Guid StudentId,
    string StudentName,
    ModerationStage Stage,
    ModerationDecision Decision,
    RiskLevel Risk,
    string? Categories,
    string? Reason,
    bool InterventionRaised,
    bool Reviewed,
    DateTimeOffset CreatedAt);

public sealed record ReviewModerationRequest(Guid EventId, ModerationDecision Decision, string? Notes);

// ---- Admin: analytics ----
public sealed record CohortAnalyticsDto(
    int TotalStudents,
    int ActiveStudents7d,
    int LessonsCompleted7d,
    int TutorSessions7d,
    int OpenModerationFlags,
    IReadOnlyList<CohortRow> ByLevel,
    IReadOnlyList<CohortRow> BySchoolType);

public sealed record CohortRow(string Key, int Students, double AvgMastery, int LessonsCompleted);

// ---- Admin: webhook logs ----
public sealed record WebhookLogDto(Guid PaymentId, PaymentProvider Provider, string ProviderPaymentId, PaymentStatus Status, int AmountCents, string Currency, DateTimeOffset? ProcessedAt, DateTimeOffset CreatedAt);

// ---- Billing ----
public sealed record CheckoutSessionRequest(Guid HouseholdId, string PlanCode, string ReturnUrl, string? PromoCode = null);
public sealed record CheckoutSessionDto(string Provider, string CheckoutUrl, string ProviderSessionId);

// ---- Promo / gift codes ----
public sealed record CreatePromoCodeRequest(string Code, int DiscountPercent, int MaxRedemptions, DateTimeOffset? ExpiresAt);
public sealed record PromoCodeDto(Guid Id, string Code, int DiscountPercent, int MaxRedemptions, int RedemptionCount, DateTimeOffset? ExpiresAt, bool IsActive);
public sealed record ValidatePromoRequest(string Code);
/// <summary>Reason is null when valid; otherwise a code: "invalid" | "expired" | "exhausted".</summary>
public sealed record PromoValidationDto(bool Valid, int DiscountPercent, string? Reason);

public sealed record SubscriptionDto(
    Guid Id,
    string PlanCode,
    string PlanName,
    SubscriptionStatus Status,
    string Currency,
    int AmountCents,
    int SeatLimit,
    DateTimeOffset CurrentPeriodEnd,
    IReadOnlyList<InvoiceDto> Invoices);

public sealed record InvoiceDto(Guid Id, string Number, InvoiceStatus Status, int AmountCents, string Currency, DateTimeOffset IssuedAt, DateTimeOffset? PaidAt, string? HostedUrl);

public sealed record BillingPlanDto(string Code, string Name, int AmountCents, string Currency, string Interval, int SeatLimit, IReadOnlyList<string> Features);

// ---- Privacy ----
public sealed record PrivacyExportRequest(Guid? StudentId);
public sealed record PrivacyDeleteRequest(Guid? StudentId, string? Reason);
public sealed record PrivacyRequestDto(Guid Id, PrivacyRequestType Type, PrivacyRequestStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, string? DownloadUrl);
