using System.Collections.Concurrent;
using Cerdik.Application.Abstractions;

namespace Cerdik.IntegrationTests;

/// <summary>Test double for <see cref="IEmailSender"/> that records sent messages so tests can assert
/// on them (e.g. extract a password-reset link) instead of hitting a real SMTP server.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    public readonly record struct Sent(string To, string Subject, string Html);

    private readonly ConcurrentQueue<Sent> _sent = new();

    public IReadOnlyList<Sent> Messages => _sent.ToArray();

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        _sent.Enqueue(new Sent(to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
