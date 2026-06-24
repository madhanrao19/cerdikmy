using System.Net;
using System.Net.Http.Json;
using Cerdik.Application.Dtos;
using Cerdik.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cerdik.IntegrationTests;

[Collection("api")]
public class LearningFlowTests
{
    private readonly ApiFactory _factory;

    public LearningFlowTests(SeededApiFixture fixture) => _factory = fixture.Factory;

    private async Task<HttpClient> LoginAsParentAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/auth/login", new LoginRequest(ApiFactory.ParentEmail, ApiFactory.ParentPassword));
        login.EnsureSuccessStatusCode();
        return client;
    }

    private async Task<Guid> FirstStudentIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<MeResponse>("/me", TestJson.Options);
        return me!.Students[0].Id;
    }

    [Fact]
    public async Task Create_tutor_session_returns_session_with_curriculum_context()
    {
        var client = await LoginAsParentAsync();
        var studentId = await FirstStudentIdAsync(client);

        var resp = await client.PostAsJsonAsync("/tutor/sessions",
            new CreateTutorSessionRequest(studentId, null, null, "Help with addition"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = await resp.Content.ReadFromJsonAsync<TutorSessionDto>(TestJson.Options);
        session!.Id.Should().NotBeEmpty();
        session.StudentId.Should().Be(studentId);
        session.CurriculumVersionCode.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Tutor_message_returns_grounded_answer_with_citations()
    {
        var client = await LoginAsParentAsync();

        // Use a session bound to the seeded SK Maths variant so retrieval has approved chunks.
        Guid studentId, variantId;
        using (var db = _factory.NewDbContext())
        {
            var student = await db.Students.FirstAsync(s => s.DisplayName == "Aisyah");
            studentId = student.Id;
            variantId = await db.SubjectVariants.Where(v => v.SchoolType == SchoolType.SK && v.Language == Language.BM)
                .Select(v => v.Id).FirstAsync();
        }

        var sessionResp = await client.PostAsJsonAsync("/tutor/sessions",
            new CreateTutorSessionRequest(studentId, variantId, null, "Maths"));
        var session = await sessionResp.Content.ReadFromJsonAsync<TutorSessionDto>(TestJson.Options);

        var reply = await client.PostAsJsonAsync($"/tutor/sessions/{session!.Id}/messages",
            new SendTutorMessageRequest("Macam mana nak kira 3 + 4?"));
        reply.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await reply.Content.ReadFromJsonAsync<TutorReplyDto>(TestJson.Options);
        dto!.AnswerMarkdown.Should().NotBeNullOrEmpty();
        dto.Citations.Should().NotBeEmpty("the SK maths lesson is indexed and retrievable");
    }

    [Fact]
    public async Task Submit_attempt_grades_and_records_progress()
    {
        var client = await LoginAsParentAsync();

        Guid activityId, studentId;
        using (var db = _factory.NewDbContext())
        {
            var student = await db.Students.FirstAsync(s => s.DisplayName == "Aisyah");
            studentId = student.Id;
            // The SK maths quiz with question "m1" (answer 7).
            activityId = await db.Activities.Where(a => a.QuestionsJson.Contains("\"m1\"")).Select(a => a.Id).FirstAsync();
        }

        var start = await client.PostAsJsonAsync($"/activities/{activityId}/start", new StartActivityRequest(studentId));
        start.StatusCode.Should().Be(HttpStatusCode.OK);
        var attempt = await start.Content.ReadFromJsonAsync<AttemptDto>(TestJson.Options);

        var answers = new Dictionary<string, string> { ["m1"] = "7", ["m2"] = "7", ["m3"] = "Palsu" };
        var submit = await client.PostAsJsonAsync($"/attempts/{attempt!.Id}/submit", new SubmitAttemptRequest(answers));
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await submit.Content.ReadFromJsonAsync<AttemptResultDto>(TestJson.Options);
        result!.MaxScore.Should().BeGreaterThan(0);
        result.Score.Should().Be(result.MaxScore, "all submitted answers are correct");
        result.Passed.Should().BeTrue();
        result.TahapPenguasaan.Should().BeOneOf(MasteryBand.TP1, MasteryBand.TP2, MasteryBand.TP3, MasteryBand.TP4, MasteryBand.TP5, MasteryBand.TP6);
    }

    [Fact]
    public async Task Parent_can_review_a_childs_tutor_conversations()
    {
        var client = await LoginAsParentAsync();
        var studentId = await FirstStudentIdAsync(client);

        var sessionResp = await client.PostAsJsonAsync("/tutor/sessions",
            new CreateTutorSessionRequest(studentId, null, null, "Review me"));
        var session = await sessionResp.Content.ReadFromJsonAsync<TutorSessionDto>(TestJson.Options);
        await client.PostAsJsonAsync($"/tutor/sessions/{session!.Id}/messages",
            new SendTutorMessageRequest("Hello tutor"));

        // List view
        var listResp = await client.GetAsync($"/parents/students/{studentId}/tutor-sessions");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaries = await listResp.Content.ReadFromJsonAsync<List<TutorSessionSummaryDto>>(TestJson.Options);
        var summary = summaries!.Single(s => s.Id == session.Id);
        summary.MessageCount.Should().BeGreaterThanOrEqualTo(2, "the user message and the assistant reply are both stored");

        // Transcript view
        var transcriptResp = await client.GetAsync($"/parents/tutor-sessions/{session.Id}");
        transcriptResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var transcript = await transcriptResp.Content.ReadFromJsonAsync<TutorSessionDto>(TestJson.Options);
        transcript!.Messages.Should().Contain(m => m.Role == TutorMessageRole.User);
        transcript.Messages.Should().Contain(m => m.Role == TutorMessageRole.Assistant);
    }

    [Fact]
    public async Task Parent_cannot_review_sessions_for_a_student_they_do_not_guardian()
    {
        var client = await LoginAsParentAsync();

        var resp = await client.GetAsync($"/parents/students/{Guid.NewGuid()}/tutor-sessions");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Standards_mastery_returns_each_standard_with_a_status()
    {
        var client = await LoginAsParentAsync();

        Guid studentId, subjectId;
        using (var db = _factory.NewDbContext())
        {
            studentId = (await db.Students.FirstAsync(s => s.DisplayName == "Aisyah")).Id;
            subjectId = await db.LearningStandards.Select(s => s.SubjectId).FirstAsync();
        }

        var resp = await client.GetAsync($"/students/{studentId}/subjects/{subjectId}/standards-mastery");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var map = await resp.Content.ReadFromJsonAsync<SubjectStandardsMasteryDto>(TestJson.Options);
        map!.SubjectId.Should().Be(subjectId);
        map.Standards.Should().NotBeEmpty("the seeded subject defines learning standards");
        map.Standards.Should().OnlyContain(s => !string.IsNullOrEmpty(s.Code));
    }

    [Fact]
    public async Task Streak_reflects_activity_done_today()
    {
        var client = await LoginAsParentAsync();

        Guid studentId;
        using (var db = _factory.NewDbContext())
        {
            var student = await db.Students.FirstAsync(s => s.DisplayName == "Aisyah");
            studentId = student.Id;
            var activityId = await db.Activities.Select(a => a.Id).FirstAsync();
            db.Attempts.Add(new Cerdik.Domain.Entities.Attempt
            {
                StudentId = studentId,
                ActivityId = activityId,
                Status = AttemptStatus.Graded,
                SubmittedAt = DateTimeOffset.UtcNow,
                Score = 1,
                MaxScore = 1,
                PercentScore = 100,
                Passed = true,
            });
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync($"/students/{studentId}/streak");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var streak = await resp.Content.ReadFromJsonAsync<StudentStreakDto>(TestJson.Options);
        streak!.ActiveToday.Should().BeTrue();
        streak.CurrentStreak.Should().BeGreaterThanOrEqualTo(1);
        streak.TodayMinutes.Should().BeGreaterThanOrEqualTo(5);
        streak.GoalMinutes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Recommendations_return_ranked_next_lessons()
    {
        var client = await LoginAsParentAsync();
        Guid studentId;
        using (var db = _factory.NewDbContext())
        {
            studentId = (await db.Students.FirstAsync(s => s.DisplayName == "Aisyah")).Id;
        }

        var resp = await client.GetAsync($"/students/{studentId}/recommendations?limit=5");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var recs = await resp.Content.ReadFromJsonAsync<List<LessonRecommendationDto>>(TestJson.Options);
        recs.Should().NotBeNull();
        recs!.Count.Should().BeLessThanOrEqualTo(5);
        // Whatever is recommended must be well-formed (an empty list is a valid result).
        recs.All(r => !string.IsNullOrEmpty(r.LessonTitle) && r.LessonId != Guid.Empty).Should().BeTrue();
    }

    [Fact]
    public async Task Placement_quiz_assembles_questions_and_grades()
    {
        var client = await LoginAsParentAsync();

        Guid studentId, subjectId;
        using (var db = _factory.NewDbContext())
        {
            studentId = (await db.Students.FirstAsync(s => s.DisplayName == "Aisyah")).Id;
            var maths = await db.Activities.Include(a => a.Lesson)
                .FirstAsync(a => a.QuestionsJson.Contains("\"m1\""));
            subjectId = await db.SubjectVariants.Where(v => v.Id == maths.Lesson.SubjectVariantId)
                .Select(v => v.SubjectId).FirstAsync();
        }

        var test = await client.GetFromJsonAsync<PlacementTestDto>(
            $"/students/{studentId}/subjects/{subjectId}/placement", TestJson.Options);
        test!.SubjectId.Should().Be(subjectId);
        test.Questions.Should().NotBeEmpty();
        test.Questions.Should().OnlyContain(q => q.Key.Contains('|'));

        // Answer the known seeded maths questions correctly (m1=7, m2=7).
        var known = new Dictionary<string, string> { ["m1"] = "7", ["m2"] = "7", ["m3"] = "Palsu" };
        var answers = test.Questions
            .Where(q => known.ContainsKey(q.Key.Split('|')[1]))
            .ToDictionary(q => q.Key, q => known[q.Key.Split('|')[1]]);

        var resp = await client.PostAsJsonAsync(
            $"/students/{studentId}/subjects/{subjectId}/placement", new PlacementSubmitRequest(answers));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await resp.Content.ReadFromJsonAsync<PlacementResultDto>(TestJson.Options);
        result!.SubjectId.Should().Be(subjectId);
        result.PercentScore.Should().BeInRange(0, 100);
        result.Total.Should().Be(test.Questions.Count, "every assembled question is graded, not only answered ones");
        result.Correct.Should().BeGreaterThan(0, "the known maths answers are correct");
    }

    [Fact]
    public async Task Mock_exam_starts_grades_and_records_history()
    {
        var client = await LoginAsParentAsync();

        Guid studentId, subjectId;
        using (var db = _factory.NewDbContext())
        {
            studentId = (await db.Students.FirstAsync(s => s.DisplayName == "Aisyah")).Id;
            var maths = await db.Activities.Include(a => a.Lesson)
                .FirstAsync(a => a.QuestionsJson.Contains("\"m1\""));
            subjectId = await db.SubjectVariants.Where(v => v.Id == maths.Lesson.SubjectVariantId)
                .Select(v => v.SubjectId).FirstAsync();
        }

        var startResp = await client.PostAsync($"/students/{studentId}/subjects/{subjectId}/exam/start", null);
        startResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var exam = await startResp.Content.ReadFromJsonAsync<ExamStartDto>(TestJson.Options);
        exam!.Questions.Should().NotBeEmpty();
        exam.DurationSeconds.Should().BeGreaterThan(0);

        var known = new Dictionary<string, string> { ["m1"] = "7", ["m2"] = "7", ["m3"] = "Palsu" };
        var answers = exam.Questions
            .Where(q => known.ContainsKey(q.Key.Split('|')[1]))
            .ToDictionary(q => q.Key, q => known[q.Key.Split('|')[1]]);

        var submitResp = await client.PostAsJsonAsync(
            $"/students/{studentId}/exam/{exam.ExamId}/submit", new ExamSubmitRequest(answers, 120));
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await submitResp.Content.ReadFromJsonAsync<ExamResultDto>(TestJson.Options);
        result!.QuestionCount.Should().Be(exam.Questions.Count);
        result.CorrectCount.Should().BeGreaterThan(0);
        result.Grade.Should().NotBeNullOrEmpty();

        var history = await client.GetFromJsonAsync<List<ExamHistoryItemDto>>(
            $"/students/{studentId}/exams", TestJson.Options);
        history!.Should().Contain(h => h.ExamId == exam.ExamId);

        // A submitted exam can't be re-submitted.
        var second = await client.PostAsJsonAsync(
            $"/students/{studentId}/exam/{exam.ExamId}/submit", new ExamSubmitRequest(answers, 120));
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reviews_surface_completed_lessons_that_are_due()
    {
        var client = await LoginAsParentAsync();

        Guid studentId, lessonId;
        using (var db = _factory.NewDbContext())
        {
            studentId = (await db.Students.FirstAsync(s => s.DisplayName == "Wei Han")).Id;
            lessonId = await db.Lessons.Select(l => l.Id).FirstAsync();
            db.ProgressRecords.Add(new Cerdik.Domain.Entities.ProgressRecord
            {
                StudentId = studentId,
                LessonId = lessonId,
                Completed = true,
                CompletedAt = DateTimeOffset.UtcNow.AddDays(-60),
                MasteryScore = 80, // TP5 -> 21-day interval; 60 days elapsed => due
                LastActivityAt = DateTimeOffset.UtcNow.AddDays(-60),
            });
            await db.SaveChangesAsync();
        }

        var reviews = await client.GetFromJsonAsync<List<ReviewItemDto>>(
            $"/students/{studentId}/reviews", TestJson.Options);
        reviews!.Should().Contain(r => r.LessonId == lessonId);
    }

    [Fact]
    public async Task Insights_return_a_projected_grade_and_risk()
    {
        var client = await LoginAsParentAsync();
        var studentId = await FirstStudentIdAsync(client);

        var resp = await client.GetAsync($"/students/{studentId}/insights");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var ins = await resp.Content.ReadFromJsonAsync<StudentInsightsDto>(TestJson.Options);
        ins!.ProjectedGrade.Should().NotBeNullOrEmpty();
        ins.ProjectedPercent.Should().BeInRange(0, 100);
        ins.Risk.Should().BeOneOf(InsightRisk.Low, InsightRisk.Medium, InsightRisk.High);
        ins.Trend.Should().BeOneOf(InsightTrend.Declining, InsightTrend.Steady, InsightTrend.Improving);
    }

    [Fact]
    public async Task Passed_exam_yields_a_printable_certificate()
    {
        var client = await LoginAsParentAsync();

        Guid studentId, subjectId;
        using (var db = _factory.NewDbContext())
        {
            studentId = (await db.Students.FirstAsync(s => s.DisplayName == "Aisyah")).Id;
            var maths = await db.Activities.Include(a => a.Lesson)
                .FirstAsync(a => a.QuestionsJson.Contains("\"m1\""));
            subjectId = await db.SubjectVariants.Where(v => v.Id == maths.Lesson.SubjectVariantId)
                .Select(v => v.SubjectId).FirstAsync();
        }

        var start = await client.PostAsync($"/students/{studentId}/subjects/{subjectId}/exam/start", null);
        var exam = await start.Content.ReadFromJsonAsync<ExamStartDto>(TestJson.Options);
        var known = new Dictionary<string, string> { ["m1"] = "7", ["m2"] = "7", ["m3"] = "Palsu" };
        var answers = exam!.Questions
            .Where(q => known.ContainsKey(q.Key.Split('|')[1]))
            .ToDictionary(q => q.Key, q => known[q.Key.Split('|')[1]]);
        await client.PostAsJsonAsync($"/students/{studentId}/exam/{exam.ExamId}/submit",
            new ExamSubmitRequest(answers, 60));

        var certResp = await client.GetAsync($"/students/{studentId}/certificates/{exam.ExamId}");
        certResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var cert = await certResp.Content.ReadFromJsonAsync<CertificateDto>(TestJson.Options);
        cert!.ExamId.Should().Be(exam.ExamId);
        cert.StudentName.Should().NotBeNullOrEmpty();
        cert.PercentScore.Should().BeGreaterThanOrEqualTo(50);

        var list = await client.GetFromJsonAsync<List<CertificateDto>>(
            $"/students/{studentId}/certificates", TestJson.Options);
        list!.Should().Contain(c => c.ExamId == exam.ExamId);
    }

    [Fact]
    public async Task Parent_dashboard_returns_children_overview()
    {
        var client = await LoginAsParentAsync();

        var resp = await client.GetAsync("/parents/dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dashboard = await resp.Content.ReadFromJsonAsync<ParentDashboardDto>(TestJson.Options);
        dashboard!.Children.Should().NotBeEmpty();
        dashboard.HouseholdName.Should().NotBeNullOrEmpty();
    }
}
