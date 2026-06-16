using System.Net;

namespace Cerdik.Web.Services;

/// <summary>
/// Thrown by <see cref="ApiClient"/> when the backend returns a non-2xx response.
/// Carries the HTTP status code and any response body so callers can surface
/// a meaningful, accessible error message to the user.
/// </summary>
public sealed class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ResponseBody { get; }

    public ApiException(HttpStatusCode statusCode, string message, string? responseBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
}
