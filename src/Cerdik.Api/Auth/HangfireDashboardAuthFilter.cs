using Cerdik.Domain;
using Hangfire.Dashboard;

namespace Cerdik.Api.Auth;

/// <summary>Restricts the Hangfire dashboard to admins (open in Development for convenience).</summary>
public sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    private readonly bool _allowAll;

    public HangfireDashboardAuthFilter(bool allowAll) => _allowAll = allowAll;

    public bool Authorize(DashboardContext context)
    {
        if (_allowAll) return true;
        var http = context.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true
               && (http.User.IsInRole(nameof(UserRole.Admin)) || http.User.IsInRole(nameof(UserRole.ContentAdmin)));
    }
}
