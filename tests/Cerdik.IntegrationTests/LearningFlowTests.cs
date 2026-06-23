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
