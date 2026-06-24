using Cerdik.Domain;

namespace Cerdik.Web.Services;

/// <summary>A single source of truth for the role-aware primary navigation, shared by the desktop
/// sidebar (<c>NavMenu</c>) and the mobile bottom bar (<c>MobileBottomNav</c>) so they never drift.
/// <see cref="LabelKey"/> is a localization key resolved via <see cref="IUiText"/>.</summary>
public sealed record NavEntry(string Href, string LabelKey, string Icon, UserRole[] Roles);

public static class NavRegistry
{
    public static readonly NavEntry[] All =
    [
        // Parent
        new("/parent", "nav.dashboard", "🏠", [UserRole.Parent]),
        new("/parent/plan", "nav.plans", "🗓️", [UserRole.Parent]),
        new("/parent/billing", "nav.billing", "💳", [UserRole.Parent]),
        new("/parent/flags", "nav.safety", "🚩", [UserRole.Parent]),
        new("/parent/tutor-review", "nav.tutor_review", "💬", [UserRole.Parent]),
        // Student
        new("/student", "nav.today", "🏠", [UserRole.Student]),
        new("/student/tutor", "nav.tutor", "💬", [UserRole.Student]),
        new("/student/progress", "nav.progress", "📈", [UserRole.Student]),
        // Admin
        new("/admin", "nav.analytics", "📊", [UserRole.Admin, UserRole.ContentAdmin]),
        new("/admin/users", "nav.users", "👥", [UserRole.Admin]),
        new("/admin/content", "nav.content", "📚", [UserRole.Admin, UserRole.ContentAdmin]),
        new("/admin/media", "nav.media", "🖼️", [UserRole.Admin, UserRole.ContentAdmin]),
        new("/admin/curriculum", "nav.curriculum", "🧭", [UserRole.Admin, UserRole.ContentAdmin]),
        new("/admin/moderation", "nav.moderation", "🛡️", [UserRole.Admin, UserRole.SafetyReviewer]),
        new("/admin/payments", "nav.payments", "🧾", [UserRole.Admin]),
        new("/admin/promo-codes", "nav.promo", "🎟️", [UserRole.Admin]),
    ];

    public static IEnumerable<NavEntry> For(UserRole? role) =>
        role is null ? Array.Empty<NavEntry>() : All.Where(i => i.Roles.Contains(role.Value));
}
