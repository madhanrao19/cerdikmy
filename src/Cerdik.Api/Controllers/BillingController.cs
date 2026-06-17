using Cerdik.Application.Abstractions;
using Cerdik.Application.Dtos;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Options;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Cerdik.Api.Controllers;

[ApiController]
[Authorize(Roles = "Parent,Admin")]
public sealed class BillingController : ControllerBase
{
    /// <summary>Static plan catalogue (cents, MYR).</summary>
    public static readonly IReadOnlyList<BillingPlanDto> Plans =
    [
        new("family-monthly", "Family Monthly", 4900, "MYR", "month", 4, ["Up to 4 learners", "All subjects & DLP variants", "AI tutor", "Progress reports"]),
        new("family-yearly", "Family Yearly", 49000, "MYR", "year", 4, ["2 months free", "Up to 4 learners", "All subjects & DLP variants", "Priority support"]),
        new("solo-monthly", "Solo Monthly", 1900, "MYR", "month", 1, ["1 learner", "All subjects", "AI tutor"]),
    ];

    private readonly AppDbContext _db;
    private readonly IPaymentProviderFactory _payments;
    private readonly ICurrentUser _current;
    private readonly PaymentOptions _opt;

    public BillingController(AppDbContext db, IPaymentProviderFactory payments, ICurrentUser current, IOptions<PaymentOptions> opt)
    {
        _db = db;
        _payments = payments;
        _current = current;
        _opt = opt.Value;
    }

    [HttpGet("/billing/plans")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<BillingPlanDto>> GetPlans() => Ok(Plans);

    [HttpPost("/billing/checkout-session")]
    public async Task<ActionResult<CheckoutSessionDto>> Checkout([FromBody] CheckoutSessionRequest req, CancellationToken ct)
    {
        var plan = Plans.FirstOrDefault(p => p.Code == req.PlanCode) ?? throw ApiException.BadRequest("Unknown plan.", "unknown_plan");

        var household = await _db.Households.FirstOrDefaultAsync(h => h.Id == req.HouseholdId, ct) ?? throw ApiException.NotFound("Household");
        if (!_current.IsInRole(UserRole.Admin))
        {
            var member = await _db.Users.AnyAsync(u => u.Id == _current.UserId && u.HouseholdId == household.Id, ct);
            if (!member) throw ApiException.Forbidden("You don't belong to this household.");
        }

        var email = _current.Email ?? "billing@cerdik.my";
        var providerEnum = Enum.TryParse<PaymentProvider>(_opt.Provider, ignoreCase: true, out var pe) ? pe : PaymentProvider.Billplz;
        var provider = _payments.Resolve(providerEnum);

        var session = await provider.CreateCheckoutSessionAsync(
            new CheckoutRequest(household.Id, plan.Code, plan.AmountCents, plan.Currency, email, req.ReturnUrl), ct);

        // Persist the checkout -> subscription mapping NOW so the webhook can resolve the exact
        // household by ProviderSubscriptionId, never a global "newest row" guess. One subscription
        // per household: reuse the existing row, otherwise create a pending (Trialing) one.
        var subscription = await _db.Subscriptions
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(s => s.HouseholdId == household.Id, ct);
        if (subscription is null)
        {
            subscription = new Subscription { HouseholdId = household.Id, Status = SubscriptionStatus.Trialing };
            _db.Subscriptions.Add(subscription);
        }
        subscription.PlanCode = plan.Code;
        subscription.PlanName = plan.Name;
        subscription.Currency = plan.Currency;
        subscription.AmountCents = plan.AmountCents;
        subscription.SeatLimit = plan.SeatLimit;
        subscription.Provider = providerEnum;
        subscription.ProviderSubscriptionId = session.ProviderSessionId;
        await _db.SaveChangesAsync(ct);

        return Ok(new CheckoutSessionDto(providerEnum.ToString(), session.CheckoutUrl, session.ProviderSessionId));
    }
}
