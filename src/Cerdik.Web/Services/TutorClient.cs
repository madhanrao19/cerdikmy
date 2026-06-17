using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Cerdik.Application.Dtos;

namespace Cerdik.Web.Services;

/// <summary>An incremental event from the tutor SSE stream.</summary>
public readonly record struct TutorStreamEvent(TutorStreamEventKind Kind, string? Delta, TutorReplyDto? Final);

public enum TutorStreamEventKind
{
    Delta,
    Final,
}

/// <summary>
/// Reads the Server-Sent Events stream from
/// <c>POST /tutor/sessions/{id}/messages/stream</c>, yielding "delta" text chunks
/// as they arrive and a terminal "final" event carrying the complete
/// <see cref="TutorReplyDto"/> (citations, mastery signal, risk, etc.).
/// </summary>
public sealed class TutorClient
{
    private readonly HttpClient _http;
    private readonly AccessTokenProvider _tokens;

    public TutorClient(HttpClient http, AccessTokenProvider tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    public async IAsyncEnumerable<TutorStreamEvent> StreamMessageAsync(
        Guid sessionId,
        string content,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tutor/sessions/{sessionId}/messages/stream")
        {
            Content = JsonContent.Create(new SendTutorMessageRequest(content), options: ApiClient.JsonOptions),
        };
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (!string.IsNullOrEmpty(_tokens.Token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _tokens.Token);
        }

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new ApiException(response.StatusCode, "The tutor stream could not be started.", body);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? eventName = null;
        var data = new StringBuilder();

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            // A blank line dispatches the accumulated event (per the SSE spec).
            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    var evt = ParseEvent(eventName, data.ToString());
                    if (evt is { } parsed) yield return parsed;
                }
                eventName = null;
                data.Clear();
                continue;
            }

            if (line.StartsWith(':')) continue; // comment / heartbeat

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line["data:".Length..].TrimStart());
            }
        }

        // Flush any trailing event that wasn't terminated by a blank line.
        if (data.Length > 0)
        {
            var evt = ParseEvent(eventName, data.ToString());
            if (evt is { } parsed) yield return parsed;
        }
    }

    private static TutorStreamEvent? ParseEvent(string? eventName, string data)
    {
        switch (eventName)
        {
            case "delta":
            {
                var text = TryExtractDeltaText(data);
                return new TutorStreamEvent(TutorStreamEventKind.Delta, text, null);
            }
            case "final":
            {
                var reply = JsonSerializer.Deserialize<TutorReplyDto>(data, ApiClient.JsonOptions);
                return reply is null ? null : new TutorStreamEvent(TutorStreamEventKind.Final, null, reply);
            }
            default:
                return null; // ignore unknown event types
        }
    }

    private static string? TryExtractDeltaText(string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("text", out var textEl))
            {
                return textEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Some servers send raw text in the data field; fall back to it.
            return data;
        }
        return data;
    }
}
