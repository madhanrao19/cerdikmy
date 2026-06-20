using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Application.Ai;
using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Cerdik.Infrastructure.Ai;

/// <summary>Anthropic Messages API adapter with SSE streaming.</summary>
public sealed class AnthropicProvider : IAiProvider
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private readonly HttpClient _http;
    private readonly AiOptions _opt;

    public AnthropicProvider(HttpClient http, IOptions<AiOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public string Name => "anthropic";

    public async Task<TutorReply> GenerateTutorReplyAsync(TutorPrompt prompt, CancellationToken ct = default)
    {
        var answer = await CompleteAsync(prompt.SystemPrompt, BuildUserMessage(prompt), ct);
        return new TutorReply
        {
            AnswerMarkdown = answer,
            Citations = TutorReplyComposer.CitationsFromContext(prompt),
            MasterySignal = TutorReplyComposer.EstimateMastery(prompt),
            NeedsReview = false,
            Model = _opt.AnthropicChatModel,
        };
    }

    public async IAsyncEnumerable<TutorStreamChunk> StreamTutorReplyAsync(
        TutorPrompt prompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var full = new StringBuilder();
        await foreach (var delta in StreamAsync(prompt.SystemPrompt, BuildUserMessage(prompt), ct))
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
            Model = _opt.AnthropicChatModel,
        });
    }

    // Model-based moderation: ask the model to classify risk as JSON, parse it, and fall back to the
    // deterministic Heuristics layer if the key is unset or the call/parse fails (safety net).
    public async Task<RiskClassification> ClassifyRiskAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.AnthropicApiKey))
        {
            return Heuristics.ClassifyRisk(text);
        }
        try
        {
            var content = await CompleteAsync(SystemPrompts.RiskClassifier, text, ct);
            if (RiskParsing.ParseClassifierJson(content) is { } parsed)
            {
                return parsed;
            }
        }
        catch
        {
            // Fall back to deterministic heuristics on any failure.
        }
        return Heuristics.ClassifyRisk(text);
    }

    public async Task<PracticeSet> GeneratePracticeSetAsync(PracticeRequest request, CancellationToken ct = default)
    {
        var system = SystemPrompts.PracticeGenerator(request.SubjectName, request.Level, request.Language);
        var text = await CompleteAsync(system, $"Standard {request.StandardCode}: {request.StandardDescription}. {request.Count} questions.", ct);
        return new PracticeSet($"Practice — {request.SubjectName}",
            [new PracticeQuestion(text, Domain.QuestionType.ShortAnswer, Array.Empty<string>(), "See solution.", request.StandardCode)]);
    }

    public async Task<ProgressSummary> SummarizeProgressAsync(ProgressSummaryRequest request, CancellationToken ct = default)
    {
        var system = SystemPrompts.ProgressSummarizer(request.Language);
        var user = string.Join("\n", request.Subjects.Select(s => $"{s.SubjectName}: {s.Band}, {s.MasteryScore:0}%"));
        var text = await CompleteAsync(system, user, ct);
        return new ProgressSummary(text, ["Focus on the lowest-mastery subject this week."]);
    }

    private static string BuildUserMessage(TutorPrompt prompt)
    {
        var sb = new StringBuilder();
        if (prompt.Context.Count > 0)
        {
            sb.AppendLine("CONTEXT (cite as [n]):");
            var n = 1;
            foreach (var c in prompt.Context) { sb.AppendLine($"[{n}] ({c.LessonTitle}) {c.Content}"); n++; }
            sb.AppendLine();
        }
        sb.AppendLine($"STUDENT QUESTION: {prompt.StudentQuestion}");
        return sb.ToString();
    }

    private HttpRequestMessage BuildRequest(string system, string user, bool stream)
    {
        var payload = new
        {
            model = _opt.AnthropicChatModel,
            max_tokens = 1024,
            system,
            stream,
            messages = new object[] { new { role = "user", content = user } },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(payload) };
        req.Headers.Add("x-api-key", _opt.AnthropicApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        return req;
    }

    private async Task<string> CompleteAsync(string system, string user, CancellationToken ct)
    {
        using var req = BuildRequest(system, user, stream: false);
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var blocks = doc.RootElement.GetProperty("content");
        var sb = new StringBuilder();
        foreach (var block in blocks.EnumerateArray())
        {
            if (block.GetProperty("type").GetString() == "text")
            {
                sb.Append(block.GetProperty("text").GetString());
            }
        }
        return sb.ToString();
    }

    private async IAsyncEnumerable<string> StreamAsync(string system, string user, [EnumeratorCancellation] CancellationToken ct)
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

            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "content_block_delta" &&
                    doc.RootElement.TryGetProperty("delta", out var d) &&
                    d.TryGetProperty("text", out var txt))
                {
                    delta = txt.GetString();
                }
            }
            catch (JsonException) { }

            if (!string.IsNullOrEmpty(delta)) yield return delta;
        }
    }
}
