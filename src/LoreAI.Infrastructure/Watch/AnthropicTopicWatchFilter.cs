using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Classification;

namespace LoreAI.Infrastructure.Watch;

/// <summary>
/// Symétrique à <see cref="AnthropicClassifier"/>/<see cref="AnthropicEmailLinkExtractor"/> mais pour la
/// veille (lot 9, #50) : même API, même patron (<c>HttpClient</c> brut, tool-use forcé). Un échec de
/// transport/parsing renvoie <see cref="WatchEvaluation.Fallback"/> plutôt que de déclencher une alerte —
/// même philosophie que <c>ClassificationResult.Fallback</c>. <see cref="ClassifierOptions"/> et son
/// <see cref="HttpClient"/> sont réutilisés tels quels : même fournisseur, pas de configuration dédiée à
/// dupliquer.
/// </summary>
public sealed class AnthropicTopicWatchFilter : ITopicWatchFilter
{
    private const int MaxTokens = 400;

    private readonly HttpClient _httpClient;
    private readonly ClassifierOptions _options;
    private readonly ILogger<AnthropicTopicWatchFilter> _logger;

    public AnthropicTopicWatchFilter(HttpClient httpClient, IOptions<ClassifierOptions> options, ILogger<AnthropicTopicWatchFilter> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
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

    public async Task<WatchEvaluation> EvaluateAsync(
        Item candidate,
        WatchTopic topic,
        RaindropTaxonomy taxonomy,
        IReadOnlyList<LibraryItemSummary> relatedCorpusItems,
        CancellationToken cancellationToken)
    {
        var requestBody = BuildRequestBody(candidate, topic, taxonomy, relatedCorpusItems);
        var rawResponseBody = string.Empty;

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("v1/messages", requestBody, cancellationToken);
            rawResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            if (!TryExtractToolInput(rawResponseBody, out var toolInputJson, out var extractError))
            {
                return Fallback(candidate, extractError, rawResponseBody);
            }

            return TopicWatchResponseParser.TryParse(toolInputJson, _options.Model, rawResponseBody, out var result, out var parseError)
                ? result
                : Fallback(candidate, parseError, rawResponseBody);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Évaluation de veille échouée pour l'entrée {SourceId}", candidate.SourceId);
            return WatchEvaluation.Fallback(_options.Model, $"Évaluation échouée: {ex.Message}", rawResponseBody);
        }
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        OperationCanceledException => true,
        HttpRequestException or JsonException or TimeoutException => true,
        _ => false,
    };

    private WatchEvaluation Fallback(Item candidate, string reason, string rawResponseBody)
    {
        _logger.LogWarning("Évaluation de veille inexploitable pour l'entrée {SourceId} : {Reason}", candidate.SourceId, reason);
        return WatchEvaluation.Fallback(_options.Model, $"Évaluation échouée: {reason}", rawResponseBody);
    }

    private object BuildRequestBody(Item candidate, WatchTopic topic, RaindropTaxonomy taxonomy, IReadOnlyList<LibraryItemSummary> relatedCorpusItems) => new
    {
        model = _options.Model,
        max_tokens = MaxTokens,
        system = TopicWatchPromptBuilder.SystemPrompt,
        messages = new[]
        {
            new { role = "user", content = TopicWatchPromptBuilder.BuildUserMessage(candidate, topic, taxonomy, relatedCorpusItems) }
        },
        tools = new[]
        {
            new
            {
                name = TopicWatchPromptBuilder.ToolName,
                description = "Évalue si une entrée candidate de veille correspond à un sujet suivi et si elle apporte une information nouvelle par rapport au corpus déjà connu.",
                input_schema = JsonSerializer.Deserialize<JsonElement>(TopicWatchPromptBuilder.BuildToolInputSchemaJson()),
            }
        },
        tool_choice = new { type = "tool", name = TopicWatchPromptBuilder.ToolName }
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
