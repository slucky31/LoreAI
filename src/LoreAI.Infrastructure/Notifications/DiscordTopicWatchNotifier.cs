using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>
/// Alerte de veille (C4, lot 9, #50), réutilisant le même webhook Discord que <see cref="DiscordNotifier"/>
/// (roadmap : « Alerte unitaire, immédiate → Discord, existant, inchangé », pas de canal dédié). N'échoue
/// jamais bruyamment, même philosophie.
/// </summary>
public sealed class DiscordTopicWatchNotifier : ITopicWatchNotifier
{
    private readonly HttpClient _httpClient;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordTopicWatchNotifier> _logger;

    public DiscordTopicWatchNotifier(HttpClient httpClient, IOptions<DiscordOptions> options, ILogger<DiscordTopicWatchNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyAsync(Item candidate, WatchEvaluation evaluation, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { content = FormatMessage(candidate, evaluation) };
            var response = await _httpClient.PostAsJsonAsync(_options.WebhookUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Échec de l'envoi de l'alerte de veille Discord pour l'entrée {SourceId}", candidate.SourceId);
        }
    }

    private static string FormatMessage(Item candidate, WatchEvaluation evaluation) =>
        $"**[Veille — {evaluation.MatchedTopic}] {candidate.Title}**\n" +
        $"{evaluation.Reason}\n" +
        $"{candidate.Url}";
}
