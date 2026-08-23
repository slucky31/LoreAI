using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>
/// Compte-rendu de fin de cycle (issue #31), un envoi par cycle ayant traité au moins un article.
/// N'échoue jamais bruyamment : une panne Discord ne doit pas faire échouer le cycle qu'elle rapporte.
/// </summary>
public sealed class DiscordCycleReportNotifier : ICycleReportNotifier
{
    private readonly HttpClient _httpClient;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordCycleReportNotifier> _logger;

    public DiscordCycleReportNotifier(HttpClient httpClient, IOptions<DiscordOptions> options, ILogger<DiscordCycleReportNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task NotifyCycleCompletedAsync(CycleRun run, CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { content = FormatMessage(run) };
            var response = await _httpClient.PostAsJsonAsync(_options.WebhookUrl, payload, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Échec de l'envoi du compte-rendu de cycle sur Discord.");
        }
    }

    private static string FormatMessage(CycleRun run)
    {
        var stayed = run.ItemsProcessed - run.Moved;
        var header = run.Outcome == CycleOutcome.Interrupted
            ? $"**Cycle interrompu — {run.ItemsProcessed.ToString(CultureInfo.InvariantCulture)}/{run.ItemsSeen.ToString(CultureInfo.InvariantCulture)} articles traités**"
            : $"**Cycle terminé — {run.ItemsProcessed.ToString(CultureInfo.InvariantCulture)}/{run.ItemsSeen.ToString(CultureInfo.InvariantCulture)} articles traités**";

        var body = $"Déplacés : {run.Moved.ToString(CultureInfo.InvariantCulture)} — " +
                   $"Restés dans « Non trié » : {stayed.ToString(CultureInfo.InvariantCulture)} — " +
                   $"Tags ajoutés : {run.TagsApplied.ToString(CultureInfo.InvariantCulture)}";

        return run.FailureReason is null
            ? $"{header}\n{body}"
            : $"{header}\n{body}\n⚠️ {run.FailureReason}";
    }
}
