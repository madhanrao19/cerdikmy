using Cerdik.Domain.Entities;

namespace Cerdik.Application.Billing;

/// <summary>Validation for promo/gift codes, shared by the validate endpoint and checkout so the
/// rules can't drift. Returns a reason code (null when valid) for the UI to localize.</summary>
public static class PromoEvaluation
{
    public static (bool Valid, int DiscountPercent, string? Reason) Evaluate(PromoCode? code, DateTimeOffset now)
    {
        if (code is null || !code.IsActive || code.DeletedAt is not null)
        {
            return (false, 0, "invalid");
        }
        if (code.ExpiresAt is { } expires && expires < now)
        {
            return (false, 0, "expired");
        }
        if (code.MaxRedemptions > 0 && code.RedemptionCount >= code.MaxRedemptions)
        {
            return (false, 0, "exhausted");
        }
        return (true, code.DiscountPercent, null);
    }

    /// <summary>Normalised lookup form (trimmed, upper-cased).</summary>
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}
