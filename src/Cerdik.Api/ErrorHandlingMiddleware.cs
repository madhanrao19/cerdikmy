using System.Text.Json;

namespace Cerdik.Api;

/// <summary>Translates uncaught exceptions into the platform's { error, code } envelope.</summary>
public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _log;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> log)
    {
        _next = next;
        _log = log;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ApiException ex)
        {
            await Write(context, ex.StatusCode, ex.Message, ex.Code);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unhandled exception");
            await Write(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", "internal_error");
        }
    }

    private static async Task Write(HttpContext context, int status, string error, string code)
    {
        if (context.Response.HasStarted) return; // can't rewrite a streamed (SSE) response
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error, code }));
    }
}

/// <summary>A domain/application error that maps to an HTTP status + { error, code } body.</summary>
public sealed class ApiException : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public ApiException(int statusCode, string message, string code) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public static ApiException NotFound(string what) => new(StatusCodes.Status404NotFound, $"{what} not found.", "not_found");
    public static ApiException BadRequest(string message, string code = "bad_request") => new(StatusCodes.Status400BadRequest, message, code);
    public static ApiException Unauthorized(string message = "Not authenticated.") => new(StatusCodes.Status401Unauthorized, message, "unauthorized");
    public static ApiException Forbidden(string message = "Not allowed.") => new(StatusCodes.Status403Forbidden, message, "forbidden");
    public static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, message, "conflict");
}
