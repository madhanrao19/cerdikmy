using Cerdik.Domain.Common;

namespace Cerdik.Domain.Entities;

/// <summary>A household subscription to a plan.</summary>
public class Subscription : BaseEntity
{
    public Guid HouseholdId { get; set; }
    public Household Household { get; set; } = default!;

    public string PlanCode { get; set; } = "family-monthly";
    public string PlanName { get; set; } = "Family Monthly";
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trialing;
    public string Currency { get; set; } = "MYR";
    public int AmountCents { get; set; }
    public int SeatLimit { get; set; } = 4;

    public DateTimeOffset CurrentPeriodStart { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CurrentPeriodEnd { get; set; } = DateTimeOffset.UtcNow.AddMonths(1);
    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }

    public PaymentProvider Provider { get; set; } = PaymentProvider.Billplz;
    public string? ProviderSubscriptionId { get; set; }

    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

/// <summary>An invoice issued against a subscription.</summary>
public class Invoice : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = default!;

    public string Number { get; set; } = default!;
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Open;
    public string Currency { get; set; } = "MYR";
    public int AmountCents { get; set; }
    public int TaxCents { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.UtcNow.AddDays(14);
    public string? HostedUrl { get; set; }
}

/// <summary>A payment attempt/record from a provider webhook.</summary>
public class Payment : BaseEntity
{
    public Guid SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = default!;

    public Guid? InvoiceId { get; set; }

    public PaymentProvider Provider { get; set; }
    public string ProviderPaymentId { get; set; } = default!;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string Currency { get; set; } = "MYR";
    public int AmountCents { get; set; }

    /// <summary>Raw, redacted webhook payload kept for audit/reconciliation.</summary>
    public string? RawPayloadJson { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
