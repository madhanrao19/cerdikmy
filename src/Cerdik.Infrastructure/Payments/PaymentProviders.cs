using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cerdik.Application.Abstractions;
using Cerdik.Domain;
using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using DomainPaymentProvider = Cerdik.Domain.PaymentProvider;

namespace Cerdik.Infrastructure.Payments;

/// <summary>Resolves an <see cref="IPaymentProvider"/> by enum.</summary>
public sealed class PaymentProviderFactory : IPaymentProviderFactory
{
    private readonly IServiceProvider _sp;
    public PaymentProviderFactory(IServiceProvider sp) => _sp = sp;

    public IPaymentProvider Resolve(DomainPaymentProvider provider) =>
        _sp.GetRequiredKeyedService<IPaymentProvider>(provider);
}

/// <summary>Billplz (Malaysia) provider. Creates a bill and verifies the x-signature webhook.</summary>
public sealed class BillplzPaymentProvider : IPaymentProvider
{
    private const string ApiBase = "https://www.billplz.com/api/v3";
    private readonly HttpClient _http;
    private readonly PaymentOptions _opt;

    public BillplzPaymentProvider(HttpClient http, IOptions<PaymentOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public DomainPaymentProvider Provider => DomainPaymentProvider.Billplz;

    public async Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.BillplzApiKey))
        {
            return Sandbox.Session(Provider, request); // demoable without credentials
        }

        var form = new Dictionary<string, string>
        {
            ["collection_id"] = _opt.BillplzCollectionId,
            ["email"] = request.CustomerEmail,
            ["amount"] = request.AmountCents.ToString(),
            ["name"] = "cerdikMY subscription",
            ["description"] = request.PlanCode,
            ["callback_url"] = request.ReturnUrl,
            ["redirect_url"] = request.ReturnUrl,
        };
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/bills") { Content = new FormUrlEncodedContent(form) };
        msg.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opt.BillplzApiKey}:")));
        using var resp = await _http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var id = doc.RootElement.GetProperty("id").GetString()!;
        var url = doc.RootElement.GetProperty("url").GetString()!;
        return new CheckoutSession(id, url);
    }

    public Task<PaymentWebhookEvent> HandleWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        var fields = Sandbox.ParseForm(rawBody);
        var verified = VerifySignature(fields);
        var paid = fields.TryGetValue("paid", out var p) && p.Equals("true", StringComparison.OrdinalIgnoreCase);
        fields.TryGetValue("id", out var id);
        fields.TryGetValue("amount", out var amount);
        return Task.FromResult(new PaymentWebhookEvent(
            verified,
            paid ? PaymentStatus.Succeeded : PaymentStatus.Failed,
            id ?? Guid.NewGuid().ToString(),
            null,
            int.TryParse(amount, out var a) ? a : 0,
            "MYR",
            rawBody));
    }

    private bool VerifySignature(IReadOnlyDictionary<string, string> fields)
    {
        if (string.IsNullOrWhiteSpace(_opt.BillplzApiKey)) return true; // sandbox
        if (!fields.TryGetValue("x_signature", out var provided)) return false;
        // Billplz x-signature: HMAC-SHA256 of "key=value|key=value" (sorted, excluding x_signature).
        var source = string.Join("|", fields.Where(kv => kv.Key != "x_signature")
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}{kv.Value}"));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_opt.BillplzApiKey));
        var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(provided.ToLowerInvariant()));
    }
}

/// <summary>Curlec (Razorpay Malaysia) provider.</summary>
public sealed class CurlecPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _http;
    private readonly PaymentOptions _opt;

    public CurlecPaymentProvider(HttpClient http, IOptions<PaymentOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public DomainPaymentProvider Provider => DomainPaymentProvider.Curlec;

    public Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        // Curlec/Razorpay uses an order id + client-side checkout. Without keys we return a sandbox URL.
        if (string.IsNullOrWhiteSpace(_opt.CurlecKeyId))
        {
            return Task.FromResult(Sandbox.Session(Provider, request));
        }
        var orderId = "order_" + Guid.NewGuid().ToString("N")[..16];
        var url = $"{request.ReturnUrl}?provider=curlec&order_id={orderId}";
        return Task.FromResult(new CheckoutSession(orderId, url));
    }

    public Task<PaymentWebhookEvent> HandleWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        var verified = true;
        if (!string.IsNullOrWhiteSpace(_opt.CurlecKeySecret) && headers.TryGetValue("X-Razorpay-Signature", out var sig))
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_opt.CurlecKeySecret));
            var computed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
            verified = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(sig.ToLowerInvariant()));
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
        var evt = doc.RootElement.TryGetProperty("event", out var e) ? e.GetString() : null;
        var status = evt == "payment.captured" ? PaymentStatus.Succeeded : PaymentStatus.Pending;
        return Task.FromResult(new PaymentWebhookEvent(verified, status, "curlec_" + Guid.NewGuid().ToString("N")[..12], null, 0, "MYR", rawBody));
    }
}

/// <summary>Stripe provider with webhook signature verification.</summary>
public sealed class StripePaymentProvider : IPaymentProvider
{
    private const string ApiBase = "https://api.stripe.com/v1";
    private readonly HttpClient _http;
    private readonly PaymentOptions _opt;

    public StripePaymentProvider(HttpClient http, IOptions<PaymentOptions> opt)
    {
        _http = http;
        _opt = opt.Value;
    }

    public DomainPaymentProvider Provider => DomainPaymentProvider.Stripe;

    public async Task<CheckoutSession> CreateCheckoutSessionAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opt.StripeSecretKey))
        {
            return Sandbox.Session(Provider, request);
        }
        var form = new Dictionary<string, string>
        {
            ["mode"] = "subscription",
            ["success_url"] = request.ReturnUrl + "?status=success",
            ["cancel_url"] = request.ReturnUrl + "?status=cancel",
            ["customer_email"] = request.CustomerEmail,
            ["line_items[0][quantity]"] = "1",
            ["line_items[0][price_data][currency]"] = request.Currency.ToLowerInvariant(),
            ["line_items[0][price_data][recurring][interval]"] = "month",
            ["line_items[0][price_data][unit_amount]"] = request.AmountCents.ToString(),
            ["line_items[0][price_data][product_data][name]"] = $"cerdikMY {request.PlanCode}",
        };
        using var msg = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/checkout/sessions") { Content = new FormUrlEncodedContent(form) };
        msg.Headers.Authorization = new("Bearer", _opt.StripeSecretKey);
        using var resp = await _http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return new CheckoutSession(doc.RootElement.GetProperty("id").GetString()!, doc.RootElement.GetProperty("url").GetString()!);
    }

    public Task<PaymentWebhookEvent> HandleWebhookAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default)
    {
        var verified = VerifyStripeSignature(rawBody, headers);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawBody) ? "{}" : rawBody);
        var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        var status = type switch
        {
            "checkout.session.completed" or "invoice.paid" => PaymentStatus.Succeeded,
            "invoice.payment_failed" => PaymentStatus.Failed,
            _ => PaymentStatus.Pending,
        };
        var id = doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString()! : "evt_" + Guid.NewGuid().ToString("N")[..12];
        return Task.FromResult(new PaymentWebhookEvent(verified, status, id, null, 0, "MYR", rawBody));
    }

    private bool VerifyStripeSignature(string payload, IReadOnlyDictionary<string, string> headers)
    {
        if (string.IsNullOrWhiteSpace(_opt.StripeWebhookSecret)) return true; // sandbox
        if (!headers.TryGetValue("Stripe-Signature", out var header)) return false;

        var parts = header.Split(',', StringSplitOptions.TrimEntries);
        var ts = parts.FirstOrDefault(p => p.StartsWith("t="))?[2..];
        var v1 = parts.FirstOrDefault(p => p.StartsWith("v1="))?[3..];
        if (ts is null || v1 is null) return false;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_opt.StripeWebhookSecret));
        var signed = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{ts}.{payload}"))).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(signed), Encoding.UTF8.GetBytes(v1.ToLowerInvariant()));
    }
}

/// <summary>Sandbox helpers so the checkout flow is demoable without real provider credentials.</summary>
internal static class Sandbox
{
    public static CheckoutSession Session(DomainPaymentProvider provider, CheckoutRequest request)
    {
        var id = $"sandbox_{provider}_{Guid.NewGuid():N}".ToLowerInvariant();
        var url = $"{request.ReturnUrl}?provider={provider}&session={id}&sandbox=1&status=success";
        return new CheckoutSession(id, url);
    }

    public static Dictionary<string, string> ParseForm(string body)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(body)) return dict;
        foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx <= 0) continue;
            var key = Uri.UnescapeDataString(pair[..idx]);
            var val = Uri.UnescapeDataString(pair[(idx + 1)..]);
            dict[key] = val;
        }
        return dict;
    }
}
