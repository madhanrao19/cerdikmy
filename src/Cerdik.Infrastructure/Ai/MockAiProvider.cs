using System.Runtime.CompilerServices;
using System.Text;
using Cerdik.Application.Abstractions;
using Cerdik.Application.Ai;
using Cerdik.Domain;

namespace Cerdik.Infrastructure.Ai;

/// <summary>Fully-functional offline tutor provider. Grounds its answer in the retrieved context,
/// emits [n] citation markers, streams word-by-word, and never calls an external API.
/// This is the default provider so the platform runs end-to-end with no API keys, and it makes
/// tutor tests deterministic.</summary>
public sealed class MockAiProvider : IAiProvider
{
    public string Name => "mock";

    public Task<TutorReply> GenerateTutorReplyAsync(TutorPrompt prompt, CancellationToken ct = default)
    {
        var answer = ComposeAnswer(prompt);
        var reply = new TutorReply
        {
            AnswerMarkdown = answer,
            Citations = TutorReplyComposer.CitationsFromContext(prompt),
            MasterySignal = TutorReplyComposer.EstimateMastery(prompt),
            NeedsReview = false,
            Model = "mock-tutor-v1",
            PromptTokens = EstimateTokens(prompt.StudentQuestion),
            CompletionTokens = EstimateTokens(answer),
        };
        return Task.FromResult(reply);
    }

    public async IAsyncEnumerable<TutorStreamChunk> StreamTutorReplyAsync(
        TutorPrompt prompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var answer = ComposeAnswer(prompt);
        foreach (var delta in TutorReplyComposer.ChunkForStream(answer))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(12, ct);
            yield return new TutorStreamChunk(delta);
        }

        yield return new TutorStreamChunk(string.Empty, new TutorReply
        {
            AnswerMarkdown = answer,
            Citations = TutorReplyComposer.CitationsFromContext(prompt),
            MasterySignal = TutorReplyComposer.EstimateMastery(prompt),
            NeedsReview = false,
            Model = "mock-tutor-v1",
            PromptTokens = EstimateTokens(prompt.StudentQuestion),
            CompletionTokens = EstimateTokens(answer),
        });
    }

    public Task<RiskClassification> ClassifyRiskAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(Heuristics.ClassifyRisk(text));

    public Task<PracticeSet> GeneratePracticeSetAsync(PracticeRequest request, CancellationToken ct = default)
    {
        var questions = new List<PracticeQuestion>();
        for (var i = 1; i <= request.Count; i++)
        {
            questions.Add(new PracticeQuestion(
                Prompt: $"Practice {i}: Apply the skill \"{request.StandardDescription}\" in a new example.",
                Type: QuestionType.ShortAnswer,
                Options: Array.Empty<string>(),
                CorrectAnswer: "Model answer varies — check against the worked steps in the lesson.",
                Explanation: $"This reinforces standard {request.StandardCode} for {request.SubjectName}."));
        }
        return Task.FromResult(new PracticeSet($"Practice — {request.SubjectName}", questions));
    }

    public Task<ProgressSummary> SummarizeProgressAsync(ProgressSummaryRequest request, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"**{request.StudentName}** is making steady progress. ");
        foreach (var s in request.Subjects)
        {
            sb.AppendLine($"- **{s.SubjectName}**: {s.Band} (mastery {s.MasteryScore:0}%), {s.LessonsCompleted} lessons completed.");
        }
        var recs = request.Subjects
            .OrderBy(s => s.MasteryScore)
            .Take(2)
            .Select(s => $"Spend 15 minutes/day on {s.SubjectName} to move from {s.Band} towards the next band.")
            .ToList();
        if (recs.Count == 0) recs.Add("Keep up the consistent daily practice.");
        return Task.FromResult(new ProgressSummary(sb.ToString(), recs));
    }

    private static string ComposeAnswer(TutorPrompt prompt)
    {
        var sb = new StringBuilder();
        var greeting = prompt.Language switch
        {
            Language.BM => "Bagus, jom kita selesaikan ini bersama!",
            Language.ZH => "好的，我们一起来解决这个问题！",
            Language.TA => "சரி, இதை ஒன்றாக தீர்ப்போம்!",
            _ => "Great question — let's work through it together!",
        };
        sb.AppendLine(greeting);
        sb.AppendLine();
        sb.AppendLine($"You asked: *{prompt.StudentQuestion.Trim()}*");
        sb.AppendLine();

        if (prompt.Context.Count > 0)
        {
            sb.AppendLine($"Here's how it connects to your **{prompt.SubjectName}** lesson:");
            sb.AppendLine();
            var n = 1;
            foreach (var c in prompt.Context.Take(3))
            {
                sb.AppendLine($"{n}. {TutorReplyComposer.Snippet(c.Content, 180)} [{n}]");
                n++;
            }
            sb.AppendLine();
            sb.AppendLine("**Try this step-by-step:**");
            sb.AppendLine("1. Re-read the idea above in your own words.");
            sb.AppendLine("2. Work one small example slowly.");
            sb.AppendLine("3. Check your answer, then try a harder one.");
        }
        else
        {
            sb.AppendLine("I couldn't find this in your approved lessons yet, so I can't be certain. " +
                          "Please review the lesson with a trusted adult, and try asking again. 🙂");
        }

        sb.AppendLine();
        sb.AppendLine("_Would you like a hint or a practice question?_");
        return sb.ToString();
    }

    private static int EstimateTokens(string s) => Math.Max(1, s.Length / 4);
}
