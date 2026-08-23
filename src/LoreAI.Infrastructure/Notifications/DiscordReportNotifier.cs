using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;

namespace LoreAI.Infrastructure.Notifications;

/// <summary>
/// Envoie un rapport en pièce jointe via le webhook Discord existant (#43) : pas de nouveau canal, juste
/// une requête <c>multipart/form-data</c> au lieu du JSON simple utilisé par <see cref="DiscordNotifier"/>/
/// <see cref="DiscordCycleReportNotifier"/>. N'échoue jamais bruyamment : une panne Discord ne doit pas
/// faire échouer le job qui a calculé le rapport.
/// </summary>
public sealed class DiscordReportNotifier : IReportNotifier
{
    private readonly HttpClient _httpClient;
    private readonly DiscordOptions _options;
    private readonly ILogger<DiscordReportNotifier> _logger;

    public DiscordReportNotifier(HttpClient httpClient, IOptions<DiscordOptions> options, ILogger<DiscordReportNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendReportAsync(string fileName, string markdownContent, CancellationToken cancellationToken)
    {
        try
        {
            using var form = new MultipartFormDataContent();

            using var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(markdownContent));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
            form.Add(fileContent, "files[0]", fileName);

            using var payload = new StringContent(
                JsonSerializer.Serialize(new { content = "Rapport hebdomadaire LoreAI" }),
                Encoding.UTF8,
                "application/json");
            form.Add(payload, "payload_json");

            var response = await _httpClient.PostAsync(_options.WebhookUrl, form, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Échec de l'envoi du rapport {FileName} sur Discord.", fileName);
        }
    }
}
