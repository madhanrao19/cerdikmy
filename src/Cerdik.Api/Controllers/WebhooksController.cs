using Cerdik.Application.Abstractions;
using Cerdik.Domain;
using Cerdik.Domain.Entities;
using Cerdik.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Cerdik.Api.Controllers;

[ApiController]
[AllowAnonymous]
[DisableRateLimiting] // payment webhooks must never be dropped by the limiter
public sealed class WebhooksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPaymentProviderFactory _payments;
    private readonly ILogger<WebhooksController> _log;

    public WebhooksController(AppDbContext db, IPaymentProviderFactory payments, ILogger<WebhooksController> log)
    {
        _db = db;
        _payments = payments;
        _log = log;
    }

    [HttpPost("/webhooks/payments/{provider}")]
    public async Task<IActionResult> Payment(string provider, CancellationToken ct)
    {
        if (!Enum.TryParse<PaymentProvider>(provider, ignoreCase: true, out var providerEnum))
        {
            throw ApiException.BadRequest("Unknown payment provider.", "unknown_provider");
        }

        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(ct);
        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

        var handler = _payments.Resolve(providerEnum);
        var evt = await handler.HandleWebhookAsync(rawBody, headers, ct);

        if (!evt.Verified)
        {
            _log.LogWarning("Rejected unverified {Provider} webhook", provider);
            throw ApiException.BadRequest("Signature verification failed.", "invalid_signature");
        }

        // Idempotency: ignore a payment we've already recorded.
        if (await _db.Payments.AnyAsync(p => p.Provider == providerEnum && p.ProviderPaymentId == evt.ProviderPaymentId, ct))
        {
            return Ok(new { status = "duplicate" });
        }

        // Resolve the EXACT subscription this event belongs to via the provider id recorded at
        // checkout (ProviderSubscriptionId). We never fall back to a global "newest row", which
        // could activate or fault another household's subscription.
        var providerRef = evt.ProviderSubscriptionId ?? evt.ProviderPaymentId;
        var subscription = string.IsNullOrEmpty(providerRef) ? null
            : await _db.Subscriptions.FirstOrDefaultAsync(s =>
                s.ProviderSubscriptionId == evt.ProviderSubscriptionId || s.ProviderSubscriptionId == evt.ProviderPaymentId, ct);

        if (subscription is null)
        {
            _log.LogWarning("Unmatched {Provider} webhook {PaymentId}; no subscription linked at checkout.", provider, evt.ProviderPaymentId);
            return Ok(new { status = "unmatched" });
        }

        {
            _db.Payments.Add(new Payment
            {
                SubscriptionId = subscription.Id,
                Provider = providerEnum,
                ProviderPaymentId = evt.ProviderPaymentId,
                Status = evt.Status,
                AmountCents = evt.AmountCents,
                Currency = evt.Currency,
                RawPayloadJson = Redact(evt.RawJson),
                ProcessedAt = DateTimeOffset.UtcNow,
            });

            if (evt.Status == PaymentStatus.Succeeded)
            {
                subscription.Status = SubscriptionStatus.Active;
                subscription.CurrentPeriodEnd = DateTimeOffset.UtcNow.AddMonths(1);
            }
            else if (evt.Status == PaymentStatus.Failed)
            {
                subscription.Status = SubscriptionStatus.PastDue;
            }
        }

        _db.AuditLogs.Add(new AuditLog
        {
            Action = "payment.webhook",
            EntityType = "Payment",
            EntityId = evt.ProviderPaymentId,
            MetadataJson = $"{{\"provider\":\"{provider}\",\"status\":\"{evt.Status}\"}}",
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { status = "ok" });
    }

    /// <summary>Keep only the first 4KB of payload and never persist obvious secret-bearing fields.</summary>
    private static string Redact(string raw) => raw.Length > 4096 ? raw[..4096] : raw;
}
