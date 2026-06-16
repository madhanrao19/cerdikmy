using Cerdik.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Cerdik.Api.Auth;

/// <summary>Cookie names + helpers for the httpOnly access/refresh token flow.</summary>
public static class AuthCookies
{
    public const string Access = "cerdik_access";
    public const string Refresh = "cerdik_refresh";

    public static void Write(HttpResponse response, TokenPair tokens, string cookieDomain)
    {
        var domain = string.IsNullOrWhiteSpace(cookieDomain) || cookieDomain == "localhost" ? null : cookieDomain;
        var secure = response.HttpContext.Request.IsHttps;

        response.Cookies.Append(Access, tokens.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Domain = domain,
            Expires = tokens.AccessExpiresAt,
            Path = "/",
        });
        response.Cookies.Append(Refresh, tokens.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Domain = domain,
            Expires = tokens.RefreshExpiresAt,
            Path = "/",
        });
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Access);
        response.Cookies.Delete(Refresh);
    }
}
