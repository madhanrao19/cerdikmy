using Cerdik.Domain;

namespace Cerdik.Web.Services;

/// <summary>A single source of truth for the role-aware primary navigation, shared by the desktop
/// sidebar (<c>NavMenu</c>) and the mobile bottom bar (<c>MobileBottomNav</c>) so they never drift.</summary>
public sealed record NavEntry(string Href, string Label, string Icon, UserRole[] Roles);

public static class NavRegistry
{
    public static readonly NavEntry[] All =
    [
        // Parent
        new("/parent", "Dashboard", "🏠", [UserRole.Parent]),
        new("/parent/plan", "Plans", "🗓️", [UserRole.Parent]),
        new("/parent/billing", "Billing", "💳", [UserRole.Parent]),
        new("/parent/flags", "Safety", "🚩", [UserRole.Parent]),
        // Student
        new("/student", "Today", "🏠", [UserRole.Student]),
        new("/student/tutor", "Tutor", "💬", [UserRole.Student]),
        new("/student/progress", "Progress", "📈", [UserRole.Student]),
        // Admin
        new("/admin", "Analytics", "📊", [UserRole.Admin, UserRole.ContentAdmin]),
        new("/admin/users", "Users", "👥", [UserRole.Admin]),
        new("/admin/content", "Content", "📚", [UserRole.Admin, UserRole.ContentAdmin]),
        new("/admin/curriculum", "Curriculum", "🧭", [UserRole.Admin, UserRole.ContentAdmin]),
        new("/admin/moderation", "Moderation", "🛡️", [UserRole.Admin, UserRole.SafetyReviewer]),
        new("/admin/payments", "Payments", "🧾", [UserRole.Admin]),
    ];

    public static IEnumerable<NavEntry> For(UserRole? role) =>
        role is null ? Array.Empty<NavEntry>() : All.Where(i => i.Roles.Contains(role.Value));
}
