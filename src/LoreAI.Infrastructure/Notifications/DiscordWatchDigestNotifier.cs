using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>
/// Résumé de fin d'exécution de la veille (lot 9, #50, redesign) : un seul message par run, remplace la
/// notification détaillée par article — chaque match crée désormais directement un raindrop. Même règle
/// « pas d'import, pas de notification » qu'<see cref="DiscordCycleReportNotifier"/> : <c>TopicWatchJob</c>
/// n'appelle celle-ci que si au moins un sujet a eu des candidats évalués.
/// </summary>
public sealed class DiscordWatchDigestNotifier : IWatchDigestNotifier
{
    private readonly HttpClient _httpClient;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordWatchDigestNotifier> _logger;

    public DiscordWatchDigestNotifier(HttpClient httpClient, IOptions<DiscordOptions> options, ILogger<DiscordWatchDigestNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyAsync(WatchRunSummary summary, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { content = FormatMessage(summary) };
            var response = await _httpClient.PostAsJsonAsync(_options.WebhookUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Échec de l'envoi du résumé de veille sur Discord.");
        }
    }

    private static string FormatMessage(WatchRunSummary summary)
    {
        var lines = summary.Topics.Select(t =>
            $"{t.TopicName} : {t.AddedCount.ToString(CultureInfo.InvariantCulture)}/{t.EvaluatedCount.ToString(CultureInfo.InvariantCulture)} articles ajoutés");

        return $"**Veille — cycle terminé**\n{string.Join('\n', lines)}";
    }
}
