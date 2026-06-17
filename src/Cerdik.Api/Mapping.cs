using Cerdik.Application.Dtos;
using Cerdik.Domain.Entities;

namespace Cerdik.Api;

/// <summary>Entity → DTO projections shared by controllers.</summary>
internal static class Mapping
{
    public static UserDto ToDto(this User u) =>
        new(u.Id, u.Email, u.FullName, u.Role, u.HouseholdId, u.StudentId, u.OrganizationId);

    public static StudentSummaryDto ToSummary(this Student s) =>
        new(s.Id, s.DisplayName, s.Avatar, s.Level, s.SchoolType, s.PrimaryLanguage, s.DlpMode, s.Points);

    public static CurriculumVersionDto ToDto(this CurriculumVersion c) =>
        new(c.Id, c.Code, c.Name, c.Level, c.EffectiveYear, c.IsActive);

    public static SchoolProfileDto ToDto(this SchoolProfile s) =>
        new(s.Id, s.Name, s.SchoolType, s.PrimaryLanguage, s.DlpMode);

    public static LearningStandardDto ToDto(this LearningStandard s) =>
        new(s.Id, s.Code, s.Strand, s.Description, s.TargetBand, s.SortOrder);

    public static SubjectDto ToDto(this Subject s) => new(
        s.Id, s.CurriculumVersionId, s.Code, s.Name, s.GradeBand, s.Level, s.SortOrder,
        s.Variants.Select(v => new SubjectVariantDto(v.Id, v.SchoolType, v.Language, v.DlpMode, v.State, v.Label)).ToList());

    public static LessonDto ToDto(this Lesson l, IReadOnlyList<LessonBlockDto> blocks) => new(
        l.Id, l.SubjectVariantId, l.Slug, l.Title, l.Summary, l.EstimatedMinutes, l.State,
        blocks,
        l.Activities.Select(a => new ActivitySummaryDto(a.Id, a.Title, a.Type, a.MaxScore, a.PassThresholdPercent)).ToList());

    public static SubscriptionDto ToDto(this Subscription s) => new(
        s.Id, s.PlanCode, s.PlanName, s.Status, s.Currency, s.AmountCents, s.SeatLimit, s.CurrentPeriodEnd,
        s.Invoices.OrderByDescending(i => i.IssuedAt)
            .Select(i => new InvoiceDto(i.Id, i.Number, i.Status, i.AmountCents, i.Currency, i.IssuedAt, i.PaidAt, i.HostedUrl)).ToList());
}
