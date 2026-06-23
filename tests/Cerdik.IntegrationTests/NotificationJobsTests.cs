using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Jobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cerdik.IntegrationTests;

/// <summary>Covers the guardian safety-alert and weekly-digest Hangfire jobs against the seeded
/// dataset, asserting on the captured outbound email.</summary>
[Collection("api")]
public class NotificationJobsTests
{
    private readonly ApiFactory _factory;

    public NotificationJobsTests(SeededApiFixture fixture) => _factory = fixture.Factory;

    private async Task RunJobAsync(Func<BackgroundJobs, Task> run)
    {
        using var scope = _factory.Services.CreateScope();
        await run(scope.ServiceProvider.GetRequiredService<BackgroundJobs>());
    }

    [Fact]
    public async Task Guardian_is_emailed_about_a_high_risk_flag_once()
    {
        Guid eventId;
        using (var db = _factory.NewDbContext())
        {
            var student = await db.Students.FirstAsync(s => s.DisplayName == "Aisyah");
            var session = new TutorSession
            {
                StudentId = student.Id,
                Title = "Flagged chat",
                CurriculumVersionCode = "KSSR-2017",
            };
            var ev = new ModerationEvent
            {
                TutorSession = session,
                Stage = ModerationStage.PostGeneration,
                Decision = ModerationDecision.Escalate,
                Risk = RiskLevel.High,
                InterventionRaised = true,
            };
            db.TutorSessions.Add(session);
            db.ModerationEvents.Add(ev);
            await db.SaveChangesAsync();
            eventId = ev.Id;
        }

        await RunJobAsync(j => j.NotifyGuardiansOfFlagsAsync());

        _factory.Email.Messages.Should().Contain(m =>
            m.To == ApiFactory.ParentEmail && m.Subject.Contains("safety", StringComparison.OrdinalIgnoreCase));

        using (var db = _factory.NewDbContext())
        {
            (await db.ModerationEvents.FirstAsync(m => m.Id == eventId)).GuardianNotifiedAt.Should().NotBeNull();
        }

        // Second run must not re-notify (the flag is already marked).
        var before = _factory.Email.Messages.Count(m => m.Subject.Contains("safety", StringComparison.OrdinalIgnoreCase));
        await RunJobAsync(j => j.NotifyGuardiansOfFlagsAsync());
        _factory.Email.Messages.Count(m => m.Subject.Contains("safety", StringComparison.OrdinalIgnoreCase)).Should().Be(before);
    }

    [Fact]
    public async Task Weekly_digest_emails_a_parent_with_activity_this_week()
    {
        using (var db = _factory.NewDbContext())
        {
            var student = await db.Students.FirstAsync(s => s.DisplayName == "Aisyah");
            var lessonId = await db.Lessons.Select(l => l.Id).FirstAsync();
            db.ProgressRecords.Add(new ProgressRecord
            {
                StudentId = student.Id,
                LessonId = lessonId,
                Completed = true,
                CompletedAt = DateTimeOffset.UtcNow,
                MasteryScore = 80,
            });
            await db.SaveChangesAsync();
        }

        await RunJobAsync(j => j.SendWeeklyParentDigestAsync());

        _factory.Email.Messages.Should().Contain(m =>
            m.To == ApiFactory.ParentEmail && m.Subject.Contains("weekly", StringComparison.OrdinalIgnoreCase));
    }
}
