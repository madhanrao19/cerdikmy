using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Application.Ai;
using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Cerdik.Infrastructure.Ai;

/// <summary>OpenAI Chat Completions adapter (also serves Azure OpenAI when <paramref name="azure"/> is set).
/// Streams prose deltas over the model's SSE channel; citations are attached from the grounded context.</summary>
public sealed class OpenAiProvider : IAiProvider
{
    private readonly HttpClient _http;
    private readonly AiOptions _opt;
    private readonly bool _azure;

    public OpenAiProvider(HttpClient http, IOptions<AiOptions> opt, bool azure = false)
    {
        _http = http;
        _opt = opt.Value;
        _azure = azure;
    }

    public string Name => _azure ? "azureopenai" : "openai";

    public async Task<TutorReply> GenerateTutorReplyAsync(TutorPrompt prompt, CancellationToken ct = default)
    {
        var answer = await CompleteAsync(prompt.SystemPrompt, BuildUserMessage(prompt), stream: false, ct);
        return new TutorReply
        {
            AnswerMarkdown = answer,
            Citations = TutorReplyComposer.CitationsFromContext(prompt),
            MasterySignal = TutorReplyComposer.EstimateMastery(prompt),
            NeedsReview = false,
            Model = ModelName,
        };
    }

    public async IAsyncEnumerable<TutorStreamChunk> StreamTutorReplyAsync(
        TutorPrompt prompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var full = new StringBuilder();
        await foreach (var delta in StreamCompletionAsync(prompt.SystemPrompt, BuildUserMessage(prompt), ct))
        {
            full.Append(delta);
            yield return new TutorStreamChunk(delta);
        }

        yield return new TutorStreamChunk(string.Empty, new TutorReply
        {
            AnswerMarkdown = full.ToString(),
            Citations = TutorReplyComposer.CitationsFromContext(prompt),
            MasterySignal = TutorReplyComposer.EstimateMastery(prompt),
            NeedsReview = false,
            Model = ModelName,
        });
    }

    // Model-based moderation via OpenAI's purpose-built /v1/moderations endpoint. The deterministic
    // Heuristics layer is the fallback (and ModerationService still takes the stricter of the two),
    // so safety never depends solely on the API call. Azure relies on its built-in content filter.
    public async Task<RiskClassification> ClassifyRiskAsync(string text, CancellationToken ct = default)
    {
        if (_azure || string.IsNullOrWhiteSpace(_opt.OpenAiApiKey))
        {
            return Heuristics.ClassifyRisk(text);
        }
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/moderations")
            {
                Content = JsonContent.Create(new { model = "omni-moderation-latest", input = text }),
            };
            req.Headers.Authorization = new("Bearer", _opt.OpenAiApiKey);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
            {
                return RiskParsing.FromOpenAiModeration(results[0]);
            }
        }
        catch
        {
            // Network/parse failure must never weaken safety — fall back to deterministic heuristics.
        }
        return Heuristics.ClassifyRisk(text);
    }

    public async Task<PracticeSet> GeneratePracticeSetAsync(PracticeRequest request, CancellationToken ct = default)
    {
        var system = SystemPrompts.PracticeGenerator(request.SubjectName, request.Level, request.Language);
        var user = $"Standard {request.StandardCode}: {request.StandardDescription}. Produce {request.Count} questions.";
        var text = await CompleteAsync(system, user, stream: false, ct);
        var q = new PracticeQuestion(text, Domain.QuestionType.ShortAnswer, Array.Empty<string>(),
            "See worked solution.", $"Aligned to {request.StandardCode}.");
        return new PracticeSet($"Practice — {request.SubjectName}", [q]);
    }

    public async Task<ProgressSummary> SummarizeProgressAsync(ProgressSummaryRequest request, CancellationToken ct = default)
    {
        var system = SystemPrompts.ProgressSummarizer(request.Language);
        var user = string.Join("\n", request.Subjects.Select(s => $"{s.SubjectName}: {s.Band}, {s.MasteryScore:0}%, {s.LessonsCompleted} lessons"));
        var text = await CompleteAsync(system, user, stream: false, ct);
        return new ProgressSummary(text, ["Review the lowest-mastery subject for 15 minutes a day."]);
    }

    private string ModelName => _azure ? _opt.AzureDeployment : _opt.OpenAiChatModel;

    private static string BuildUserMessage(TutorPrompt prompt)
    {
        var sb = new StringBuilder();
        if (prompt.Context.Count > 0)
        {
            sb.AppendLine("CONTEXT (cite as [n]):");
            var n = 1;
            foreach (var c in prompt.Context)
            {
                sb.AppendLine($"[{n}] (lesson: {c.LessonTitle}) {c.Content}");
                n++;
            }
            sb.AppendLine();
        }
        if (prompt.History.Count > 0)
        {
            sb.AppendLine("CONVERSATION SO FAR:");
            foreach (var turn in prompt.History.TakeLast(8))
            {
                sb.AppendLine($"{turn.Role}: {turn.Content}");
            }
            sb.AppendLine();
        }
        sb.AppendLine($"STUDENT QUESTION: {prompt.StudentQuestion}");
        return sb.ToString();
    }

    private HttpRequestMessage BuildRequest(string system, string user, bool stream)
    {
        var payload = new
        {
            model = ModelName,
            stream,
            temperature = 0.4,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user },
            },
        };

        HttpRequestMessage req;
        if (_azure)
        {
            var url = $"{_opt.AzureEndpoint.TrimEnd('/')}/openai/deployments/{_opt.AzureDeployment}/chat/completions?api-version=2024-06-01";
            req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            req.Headers.Add("api-key", _opt.AzureApiKey);
        }
        else
        {
            req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
            {
                Content = JsonContent.Create(payload),
            };
            req.Headers.Authorization = new("Bearer", _opt.OpenAiApiKey);
        }
        return req;
    }

    private async Task<string> CompleteAsync(string system, string user, bool stream, CancellationToken ct)
    {
        using var req = BuildRequest(system, user, stream: false);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private async IAsyncEnumerable<string> StreamCompletionAsync(
        string system, string user, [EnumeratorCancellation] CancellationToken ct)
    {
        using var req = BuildRequest(system, user, stream: true);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(s);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:")) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;

            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var choice = doc.RootElement.GetProperty("choices")[0];
                if (choice.TryGetProperty("delta", out var d) && d.TryGetProperty("content", out var c))
                {
                    delta = c.GetString();
                }
            }
            catch (JsonException) { /* skip keep-alive / partial frames */ }

            if (!string.IsNullOrEmpty(delta)) yield return delta;
        }
    }
}
