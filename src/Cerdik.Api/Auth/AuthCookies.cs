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

    public static void Clear(HttpResponse response, string cookieDomain)
    {
        // Deletion must use the same Domain/Path the cookies were written with, otherwise a
        // domain-scoped refresh cookie survives logout and can restore the session.
        var domain = string.IsNullOrWhiteSpace(cookieDomain) || cookieDomain == "localhost" ? null : cookieDomain;
        var options = new CookieOptions { Domain = domain, Path = "/" };
        response.Cookies.Delete(Access, options);
        response.Cookies.Delete(Refresh, options);
    }
}
