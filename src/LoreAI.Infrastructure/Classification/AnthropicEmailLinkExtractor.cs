using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Classification;

/// <summary>
/// Symétrique à <see cref="AnthropicClassifier"/> mais en amont (lot 8, #49) : même API, même patron
/// (<c>HttpClient</c> brut, tool-use forcé), un échec de transport/parsing renvoie une liste vide plutôt
/// que de bloquer l'ingestion — même philosophie que <c>ClassificationResult.Fallback</c>. Le compte
/// <see cref="ClassifierOptions"/> et son <see cref="HttpClient"/> sont réutilisés tels quels : même
/// fournisseur, pas de configuration dédiée à dupliquer.
/// </summary>
public sealed class AnthropicEmailLinkExtractor : IEmailLinkExtractor
{
    private const int MaxTokens = 800;

    private readonly HttpClient _httpClient;
    private readonly ClassifierOptions _options;
    private readonly IEmailExtractionLogRepository _extractionLogRepository;
    private readonly ILogger<AnthropicEmailLinkExtractor> _logger;

    public AnthropicEmailLinkExtractor(
        HttpClient httpClient,
        IOptions<ClassifierOptions> options,
        IEmailExtractionLogRepository extractionLogRepository,
        ILogger<AnthropicEmailLinkExtractor> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _extractionLogRepository = extractionLogRepository;
        _logger = logger;

        _httpClient.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        if (!_httpClient.DefaultRequestHeaders.Contains("x-api-key"))
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        }
        if (!_httpClient.DefaultRequestHeaders.Contains("anthropic-version"))
        {
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", _options.AnthropicVersion);
        }
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<ExtractedLink>> ExtractAsync(string subject, string body, IReadOnlyList<string> candidateUrls, CancellationToken cancellationToken)
    {
        // Court-circuit avant tout appel LLM : pas d'URL candidate, rien à trancher (cf. GmailIngester/
        // EmailLinkNoiseFilter). Ne compte pas comme un appel pour S6, aucune réponse à journaliser.
        if (candidateUrls.Count == 0)
        {
            return [];
        }

        var requestBody = BuildRequestBody(subject, body, candidateUrls);
        var rawResponseBody = string.Empty;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("v1/messages", requestBody, cancellationToken);
            rawResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            await RecordUsageAsync(rawResponseBody, cancellationToken);

            if (!TryExtractToolInput(rawResponseBody, out var toolInputJson, out var extractError))
            {
                _logger.LogWarning("Extraction de liens inexploitable : {Reason}", extractError);
                return [];
            }

            if (EmailLinkExtractionResponseParser.TryParse(toolInputJson, candidateUrls, out var result, out var parseError))
            {
                return result;
            }

            _logger.LogWarning("Extraction de liens inexploitable : {Reason}", parseError);
            return [];
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Extraction de liens échouée pour un mail.");
            return [];
        }
    }

    /// <summary>
    /// Best-effort (S6) : un échec d'écriture du journal d'usage ne doit jamais faire perdre une extraction
    /// par ailleurs réussie, même logique que <c>ICycleRunRepository.RecordAsync</c>.
    /// </summary>
    private async Task RecordUsageAsync(string rawResponseBody, CancellationToken cancellationToken)
    {
        try
        {
            await _extractionLogRepository.RecordAsync(rawResponseBody, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de l'enregistrement du journal d'usage LLM pour l'extraction de liens.");
        }
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        OperationCanceledException => true,
        HttpRequestException or JsonException or TimeoutException => true,
        _ => false,
    };

    private object BuildRequestBody(string subject, string body, IReadOnlyList<string> candidateUrls) => new
    {
        model = _options.Model,
        max_tokens = MaxTokens,
        system = EmailLinkExtractionPromptBuilder.SystemPrompt,
        messages = new[]
        {
            new { role = "user", content = EmailLinkExtractionPromptBuilder.BuildUserMessage(subject, body, candidateUrls) }
        },
        tools = new[]
        {
            new
            {
                name = EmailLinkExtractionPromptBuilder.ToolName,
                description = "Sélectionne, parmi les URLs candidates d'un mail, celles qui sont de vrais articles/outils/annonces, avec un titre court pour chacune.",
                input_schema = JsonSerializer.Deserialize<JsonElement>(EmailLinkExtractionPromptBuilder.BuildToolInputSchemaJson()),
            }
        },
        tool_choice = new { type = "tool", name = EmailLinkExtractionPromptBuilder.ToolName }
    };

    private static bool TryExtractToolInput(
        string rawResponseBody,
        [NotNullWhen(true)] out string? toolInputJson,
        [NotNullWhen(false)] out string? error)
    {
        toolInputJson = null;

        using var document = JsonDocument.Parse(rawResponseBody);
        var root = document.RootElement;

        if (root.TryGetProperty("stop_reason", out var stopReason) && stopReason.GetString() == "max_tokens")
        {
            error = $"réponse Anthropic tronquée (stop_reason=max_tokens, max_tokens={MaxTokens})";
            return false;
        }

        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            error = "réponse Anthropic sans tableau content";
            return false;
        }

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var typeElement)
                && typeElement.GetString() == "tool_use"
                && block.TryGetProperty("input", out var input))
            {
                toolInputJson = input.GetRawText();
                error = null;
                return true;
            }
        }

        error = "aucun bloc tool_use dans la réponse Anthropic";
        return false;
    }
}
