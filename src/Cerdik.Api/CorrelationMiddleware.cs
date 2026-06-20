using System.Diagnostics;
using Serilog.Context;

namespace Cerdik.Api;

/// <summary>Assigns a correlation id to every request (honouring an inbound X-Correlation-ID, else the
/// current trace id, else a new id), echoes it on the response, attaches it to the active trace, and
/// pushes it into the Serilog log context so every log line for the request carries it.</summary>
public sealed class CorrelationMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming) && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation_id", correlationId);

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
