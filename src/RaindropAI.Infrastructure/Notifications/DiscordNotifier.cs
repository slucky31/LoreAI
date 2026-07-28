using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Notifications;

/// <summary>
/// Alerte quasi temps réel, appelée item par item pendant le cycle de polling.
/// N'échoue jamais bruyamment : une panne Discord ne doit pas interrompre le traitement du batch.
/// </summary>
public sealed class DiscordNotifier : IImmediateNotifier
{
    private readonly HttpClient _httpClient;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordNotifier> _logger;

    public DiscordNotifier(HttpClient httpClient, IOptions<DiscordOptions> options, ILogger<DiscordNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyAsync(RaindropItem item, ClassificationResult classification, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { content = FormatMessage(item, classification) };
            var response = await _httpClient.PostAsJsonAsync(_options.WebhookUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Échec de l'envoi de la notification Discord pour le raindrop {RaindropId}", item.Id);
        }
    }

    private static string FormatMessage(RaindropItem item, ClassificationResult classification)
    {
        var collection = classification.SuggestedCollection ?? "(non déplacé)";
        var tags = classification.Tags.Count > 0 ? string.Join(", ", classification.Tags) : "(aucun)";

        return $"**[{classification.Action}] {item.Title}**\n" +
               $"Collection : {collection} — Tags : {tags} — Priorité : {classification.Priority}\n" +
               $"{classification.Reason}\n" +
               $"{item.Link}";
    }
}
