using System.Text;
using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Application.Ai;
using Cerdik.Application.Dtos;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

[ApiController]
[Authorize]
[Route("tutor")]
[EnableRateLimiting(RateLimitingSetup.Tutor)]
public sealed class TutorController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAiProviderFactory _ai;
    private readonly IVectorRetriever _retriever;
    private readonly IModerationService _moderation;

    public TutorController(
        AppDbContext db, ICurrentUser current, IAiProviderFactory ai,
        IVectorRetriever retriever, IModerationService moderation)
    {
        _db = db;
        _current = current;
        _ai = ai;
        _retriever = retriever;
        _moderation = moderation;
    }

    [HttpPost("sessions")]
    public async Task<ActionResult<TutorSessionDto>> CreateSession([FromBody] CreateTutorSessionRequest req, CancellationToken ct)
    {
        await EnsureStudentAccess(req.StudentId, ct);

        string curriculumCode = "KSSR-2017";
        SchoolType schoolType = SchoolType.SK;
        Language language = Language.BM;
        DlpMode dlp = DlpMode.None;

        if (req.SubjectVariantId is { } variantId)
        {
            var v = await _db.SubjectVariants.Include(x => x.Subject).ThenInclude(s => s.CurriculumVersion)
                .FirstOrDefaultAsync(x => x.Id == variantId, ct) ?? throw ApiException.NotFound("Subject variant");
            curriculumCode = v.Subject.CurriculumVersion.Code;
            schoolType = v.SchoolType;
            language = v.Language;
            dlp = v.DlpMode;
        }
        else
        {
            var student = await _db.Students.FirstAsync(s => s.Id == req.StudentId, ct);
            schoolType = student.SchoolType;
            language = student.PrimaryLanguage;
            dlp = student.DlpMode;
        }

        var session = new TutorSession
        {
            StudentId = req.StudentId,
            SubjectVariantId = req.SubjectVariantId,
            LessonId = req.LessonId,
            Title = string.IsNullOrWhiteSpace(req.Title) ? "Tutor session" : req.Title!,
            CurriculumVersionCode = curriculumCode,
            SchoolType = schoolType,
            Language = language,
            DlpMode = dlp,
        };
        _db.TutorSessions.Add(session);
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(session));
    }

    [HttpPost("sessions/{id:guid}/messages")]
    public async Task<ActionResult<TutorReplyDto>> SendMessage(Guid id, [FromBody] SendTutorMessageRequest req, CancellationToken ct)
    {
        var session = await LoadSession(id, ct);
        Validate.NotEmpty(req.Content, "Message");

        var (assistant, risk) = await ProcessTurnAsync(session, req.Content, streamWriter: null, ct);

        return Ok(new TutorReplyDto(
            assistant.Id, assistant.Content,
            assistant.Citations.Select((c, i) => new CitationDto(c.EmbeddingChunkId, c.LessonId, c.LessonTitle, c.Snippet, c.Score, c.Ordinal)).ToList(),
            assistant.MasterySignal, assistant.NeedsReview, risk));
    }

    /// <summary>SSE streaming variant. Emits `delta` events as the answer is produced, then a `final`
    /// event carrying the full <see cref="TutorReplyDto"/>.</summary>
    [HttpPost("sessions/{id:guid}/messages/stream")]
    public async Task StreamMessage(Guid id, [FromBody] SendTutorMessageRequest req, CancellationToken ct)
    {
        var session = await LoadSession(id, ct);

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no"; // disable proxy buffering for SSE

        if (string.IsNullOrWhiteSpace(req.Content))
        {
            await WriteEvent("error", new { error = "Message is required.", code = "required" }, ct);
            return;
        }

        async Task Writer(string delta) => await WriteEvent("delta", new { text = delta }, ct);

        var (assistant, risk) = await ProcessTurnAsync(session, req.Content, Writer, ct);

        var dto = new TutorReplyDto(
            assistant.Id, assistant.Content,
            assistant.Citations.Select(c => new CitationDto(c.EmbeddingChunkId, c.LessonId, c.LessonTitle, c.Snippet, c.Score, c.Ordinal)).ToList(),
            assistant.MasterySignal, assistant.NeedsReview, risk);
        await WriteEvent("final", dto, ct);
    }

    // ---- core turn pipeline: pre-moderation -> retrieval -> generation -> post-moderation -> persist ----
    private async Task<(TutorMessage Assistant, RiskLevel Risk)> ProcessTurnAsync(
        TutorSession session, string content, Func<string, Task>? streamWriter, CancellationToken ct)
    {
        var userMessage = new TutorMessage { TutorSessionId = session.Id, Role = TutorMessageRole.User, Content = content };
        _db.TutorMessages.Add(userMessage);

        // 1) Pre-generation moderation.
        var pre = await _moderation.ScreenAsync(content, ModerationStage.PreGeneration, ct);
        RecordModeration(session, userMessage.Id, ModerationStage.PreGeneration, pre);

        if (!pre.Allowed)
        {
            var refusal = SafeRefusal(session.Language);
            if (streamWriter is not null) await streamWriter(refusal);
            var blockedAssistant = PersistAssistant(session, refusal, null, needsReview: true, Array.Empty<Citation>());
            await _db.SaveChangesAsync(ct);
            return (blockedAssistant, pre.Risk);
        }

        // 2) Retrieval (RAG) — only when the session is bound to a subject variant.
        IReadOnlyList<RetrievedChunk> context = Array.Empty<RetrievedChunk>();
        Guid? subjectId = session.SubjectVariantId is { } vid
            ? await _db.SubjectVariants.Where(v => v.Id == vid).Select(v => (Guid?)v.SubjectId).FirstOrDefaultAsync(ct)
            : null;

        if (subjectId is { } sid)
        {
            context = await _retriever.RetrieveAsync(new RetrievalQuery
            {
                QueryText = content,
                CurriculumVersionCode = session.CurriculumVersionCode,
                SubjectId = sid,
                SchoolType = session.SchoolType,
                Language = session.Language,
                DlpMode = session.DlpMode,
                TopK = 4,
                // The offline embedding is coarse; keep top-k grounding rather than threshold it away.
                MinScore = 0.0,
            }, ct);
        }

        // 3) Build the prompt and generate.
        var student = await _db.Students.FirstAsync(s => s.Id == session.StudentId, ct);
        var subjectName = session.SubjectVariantId is null ? "General"
            : await _db.SubjectVariants.Where(v => v.Id == session.SubjectVariantId).Select(v => v.Subject.Name).FirstOrDefaultAsync(ct) ?? "General";

        var history = await _db.TutorMessages.Where(m => m.TutorSessionId == session.Id && m.Id != userMessage.Id)
            .OrderBy(m => m.CreatedAt).Take(12)
            .Select(m => new TutorTurn(m.Role, m.Content)).ToListAsync(ct);

        var prompt = new TutorPrompt
        {
            StudentQuestion = content,
            Level = student.Level,
            Language = session.Language,
            SubjectName = subjectName,
            History = history,
            Context = context,
            SystemPrompt = SystemPrompts.TutorSystem(student.Level, session.Language, subjectName),
        };

        var provider = _ai.Resolve();
        TutorReply reply;
        if (streamWriter is not null)
        {
            var sb = new StringBuilder();
            TutorReply? final = null;
            await foreach (var chunk in provider.StreamTutorReplyAsync(prompt, ct))
            {
                if (chunk.IsFinal) { final = chunk.Final; break; }
                sb.Append(chunk.DeltaMarkdown);
                await streamWriter(chunk.DeltaMarkdown);
            }
            reply = final ?? new TutorReply { AnswerMarkdown = sb.ToString(), Citations = TutorReplyComposerCitations(context) };
        }
        else
        {
            reply = await provider.GenerateTutorReplyAsync(prompt, ct);
        }

        // 4) Post-generation moderation on the produced answer.
        var post = await _moderation.ScreenAsync(reply.AnswerMarkdown, ModerationStage.PostGeneration, ct);
        RecordModeration(session, null, ModerationStage.PostGeneration, post);

        var answer = post.Allowed ? reply.AnswerMarkdown : SafeRefusal(session.Language);
        var needsReview = reply.NeedsReview || !post.Allowed || pre.RaiseIntervention || post.RaiseIntervention;

        // 5) Persist assistant message + citations.
        var citations = post.Allowed
            ? reply.Citations.Select((c, i) => new Citation
            {
                EmbeddingChunkId = c.ChunkId,
                LessonId = c.LessonId,
                LessonTitle = c.LessonTitle,
                Snippet = c.Snippet,
                Score = c.Score,
                Ordinal = i + 1,
            }).ToList()
            : new List<Citation>();

        var assistant = PersistAssistant(session, answer, reply.MasterySignal, needsReview, citations);
        assistant.ModelUsed = reply.Model;

        // 6) Update session safety state.
        var highest = (RiskLevel)Math.Max((int)pre.Risk, (int)post.Risk);
        if ((int)highest > (int)session.HighestRisk) session.HighestRisk = highest;
        if (needsReview) session.NeedsReview = true;

        await _db.SaveChangesAsync(ct);
        return (assistant, highest);
    }

    private static IReadOnlyList<AiCitation> TutorReplyComposerCitations(IReadOnlyList<RetrievedChunk> context) =>
        context.Select(c => new AiCitation(c.ChunkId, c.LessonId, c.LessonTitle,
            c.Content.Length > 240 ? c.Content[..240] + "…" : c.Content, c.Score)).ToList();

    private TutorMessage PersistAssistant(TutorSession session, string answer, MasteryBand? mastery, bool needsReview, IReadOnlyList<Citation> citations)
    {
        var assistant = new TutorMessage
        {
            TutorSessionId = session.Id,
            Role = TutorMessageRole.Assistant,
            Content = answer,
            MasterySignal = mastery,
            NeedsReview = needsReview,
        };
        foreach (var c in citations) assistant.Citations.Add(c);
        _db.TutorMessages.Add(assistant);
        return assistant;
    }

    private void RecordModeration(TutorSession session, Guid? messageId, ModerationStage stage, ModerationOutcome outcome)
    {
        if (outcome.Decision == ModerationDecision.Allow && !outcome.RaiseIntervention) return;
        _db.ModerationEvents.Add(new ModerationEvent
        {
            TutorSessionId = session.Id,
            TutorMessageId = messageId,
            Stage = stage,
            Decision = outcome.Decision,
            Risk = outcome.Risk,
            Categories = string.Join(",", outcome.Categories),
            Reason = outcome.Reason,
            InterventionRaised = outcome.RaiseIntervention,
        });
    }

    private static string SafeRefusal(Language lang) => lang switch
    {
        Language.BM => "Maaf, cikgu tak boleh bantu dengan soalan itu. Jom kita fokus pada pelajaran. Jika ada sesuatu yang mengganggu kamu, sila beritahu ibu bapa atau orang dewasa yang dipercayai. 💙",
        Language.ZH => "对不起，老师不能帮你回答这个问题。我们专注在学习上吧。如果有让你困扰的事，请告诉父母或信任的大人。💙",
        Language.TA => "மன்னிக்கவும், இந்தக் கேள்விக்கு உதவ முடியாது. படிப்பில் கவனம் செலுத்துவோம். உங்களைத் தொந்தரவு செய்தால், பெற்றோரிடம் சொல்லுங்கள். 💙",
        _ => "Sorry, I can't help with that. Let's focus on your lesson. If something is troubling you, please tell a parent or a trusted adult. 💙",
    };

    private async Task<TutorSession> LoadSession(Guid id, CancellationToken ct)
    {
        var session = await _db.TutorSessions.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw ApiException.NotFound("Tutor session");
        await EnsureStudentAccess(session.StudentId, ct);
        return session;
    }

    private async Task WriteEvent(string eventName, object payload, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {eventName}\n", ct);
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload, Json)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private async Task EnsureStudentAccess(Guid studentId, CancellationToken ct)
    {
        if (_current.IsInRole(UserRole.Admin, UserRole.ContentAdmin, UserRole.SafetyReviewer)) return;
        if (_current.StudentId == studentId) return;
        var ok = await _db.StudentGuardians.AnyAsync(g => g.StudentId == studentId && g.GuardianUserId == _current.UserId, ct);
        if (!ok) throw ApiException.Forbidden("You don't have access to this student.");
    }

    private static TutorSessionDto ToDto(TutorSession s) => new(
        s.Id, s.StudentId, s.SubjectVariantId, s.Title, s.CurriculumVersionCode,
        s.SchoolType, s.Language, s.DlpMode, s.NeedsReview, s.HighestRisk,
        s.Messages.OrderBy(m => m.CreatedAt).Select(m => new TutorMessageDto(
            m.Id, m.Role, m.Content, m.MasterySignal, m.NeedsReview,
            m.Citations.Select(c => new CitationDto(c.EmbeddingChunkId, c.LessonId, c.LessonTitle, c.Snippet, c.Score, c.Ordinal)).ToList(),
            m.CreatedAt)).ToList());
}
