using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>
/// Relance (L4, lot 6), déclenchée par <c>ReconciliationJob</c>. Même patron que
/// <see cref="DiscordNotifier"/> : n'échoue jamais bruyamment, une panne Discord ne doit pas
/// interrompre la passe de réconciliation.
/// </summary>
public sealed class DiscordReminderNotifier : IReminderNotifier
{
    private readonly HttpClient _httpClient;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordReminderNotifier> _logger;

    public DiscordReminderNotifier(HttpClient httpClient, IOptions<DiscordOptions> options, ILogger<DiscordReminderNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyAsync(string title, string url, int daysSinceClassified, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { content = $"**Rappel** — toujours pas traité {daysSinceClassified} jours après classification.\n{title}\n{url}" };
            var response = await _httpClient.PostAsJsonAsync(_options.WebhookUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Échec de l'envoi de la relance Discord pour {Url}", url);
        }
    }
}
