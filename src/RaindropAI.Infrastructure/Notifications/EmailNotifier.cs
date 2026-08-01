using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Notifications;

/// <summary>
/// Digest quotidien exhaustif. Envoyée une seule fois par jour : les échecs remontent à l'appelant
/// (DigestNotificationService) plutôt que d'être avalés ici.
/// </summary>
public sealed class EmailNotifier : IDigestNotifier
{
    private readonly EmailOptions _options;
    private readonly ILogger<EmailNotifier> _logger;

    public EmailNotifier(IOptions<EmailOptions> options, ILogger<EmailNotifier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendDigestAsync(IReadOnlyList<ClassifiedArticle> articles, CancellationToken cancellationToken)
    {
        if (articles.Count == 0)
        {
            return;
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_options.FromAddress));
        message.To.Add(MailboxAddress.Parse(_options.ToAddress));
        message.Subject = DigestMessageBuilder.BuildSubject(articles.Count);
        message.Body = new TextPart("html") { Text = DigestMessageBuilder.BuildHtmlBody(articles) };

        using var client = new SmtpClient();
        try
        {
            var secureSocketOptions = SmtpSecurityResolver.Resolve(_options.Security, _options.SmtpPort);
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Envoi du digest ({Count} articles) via {Host}:{Port} en {Security}.",
                    articles.Count,
                    _options.SmtpHost,
                    _options.SmtpPort,
                    secureSocketOptions);
            }

            await client.ConnectAsync(_options.SmtpHost, _options.SmtpPort, secureSocketOptions, cancellationToken);
            await client.AuthenticateAsync(_options.SmtpUser, _options.SmtpPassword, cancellationToken);
            await client.SendAsync(message, cancellationToken);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }
}
