using System.Net;
using System.Net.Mail;
using Cerdik.Application.Abstractions;
using Cerdik.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cerdik.Infrastructure.Email;

/// <summary>SMTP email sender. In dev/on-prem this targets Mailpit (smtp://localhost:1025).</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly MailOptions _opt;
    private readonly ILogger<SmtpEmailSender> _log;

    public SmtpEmailSender(IOptions<MailOptions> opt, ILogger<SmtpEmailSender> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var uri = new Uri(_opt.SmtpUrl);
        using var client = new SmtpClient(uri.Host, uri.Port <= 0 ? 25 : uri.Port)
        {
            EnableSsl = uri.Scheme.Equals("smtps", StringComparison.OrdinalIgnoreCase),
        };
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            client.Credentials = new NetworkCredential(parts[0], parts.Length > 1 ? parts[1] : string.Empty);
        }

        using var message = new MailMessage
        {
            From = new MailAddress(ParseFromAddress(_opt.From), ParseFromName(_opt.From)),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true,
        };
        message.To.Add(to);

        try
        {
            await client.SendMailAsync(message, ct);
        }
        catch (Exception ex)
        {
            // Never fail a user flow because email is down; log and continue.
            _log.LogWarning(ex, "Failed to send email to {To} ({Subject})", to, subject);
        }
    }

    private static string ParseFromAddress(string from)
    {
        var start = from.IndexOf('<');
        var end = from.IndexOf('>');
        return start >= 0 && end > start ? from[(start + 1)..end].Trim() : from.Trim();
    }

    private static string ParseFromName(string from)
    {
        var start = from.IndexOf('<');
        return start > 0 ? from[..start].Trim() : "cerdikMY";
    }
}
