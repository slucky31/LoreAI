using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Classification;

/// <summary>
/// Appel Anthropic texte libre (pas de tool-use, contrairement à <see cref="AnthropicClassifier"/>) pour la
/// narration d'un thème de la revue mensuelle (S4, lot 5).
/// </summary>
public sealed class AnthropicThemeNarrativeGenerator : IThemeNarrativeGenerator
{
    private const int MaxTokens = 600;
    private const int MaxArticlesInPrompt = 30;
    private const string FallbackNarrative = "Revue indisponible pour ce thème.";

    private const string SystemPrompt = """
        Tu rédiges la revue mensuelle d'un développeur .NET, pour un thème donné, à partir de la liste des
        articles qu'il a classés ce mois-ci dans ce thème. Écris 3 à 6 phrases en français, de style narratif
        et personnel (pas une liste à puces), qui dégagent les points marquants, les tendances ou les
        recoupements entre ces articles. Réponds uniquement par ce texte, sans titre ni préambule.
        """;

    private readonly HttpClient _httpClient;
    private readonly ClassifierOptions _options;
    private readonly ILogger<AnthropicThemeNarrativeGenerator> _logger;

    public AnthropicThemeNarrativeGenerator(HttpClient httpClient, IOptions<ClassifierOptions> options, ILogger<AnthropicThemeNarrativeGenerator> logger)
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

    public async Task<string> GenerateNarrativeAsync(string theme, IReadOnlyList<MonthlyReviewArticle> articles, CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            // D6 : Classifier__SummaryModel, posé au lot 4 comme point d'extension non consommé, trouve ici
            // son premier consommateur naturel — sinon retombe sur le modèle de classification.
            model = _options.SummaryModel ?? _options.Model,
            max_tokens = MaxTokens,
            system = SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = BuildPrompt(theme, articles) }
            }
        };

        try
        {
            using var response = await _httpClient.PostAsJsonAsync("v1/messages", requestBody, cancellationToken);
            var rawResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            return TryExtractText(rawResponseBody, out var text, out var error)
                ? text
                : Fallback(theme, error);
        }
        catch (Exception ex) when (IsTransportFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Revue narrative échouée pour le thème {Theme}", theme);
            return FallbackNarrative;
        }
    }

    private static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => false,
        OperationCanceledException => true,
        HttpRequestException or JsonException or TimeoutException => true,
        _ => false,
    };

    private string Fallback(string theme, string reason)
    {
        _logger.LogWarning("Revue narrative inexploitable pour le thème {Theme} : {Reason}", theme, reason);
        return FallbackNarrative;
    }

    private static string BuildPrompt(string theme, IReadOnlyList<MonthlyReviewArticle> articles)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Thème : {theme}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Articles classés ce mois-ci ({articles.Count}) :");
        foreach (var article in articles.Take(MaxArticlesInPrompt))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"- {article.Title} : {article.Summary ?? article.Reason ?? "(pas de résumé)"}");
        }

        return builder.ToString();
    }

    private static bool TryExtractText(string rawResponseBody, out string text, out string error)
    {
        text = string.Empty;
        error = string.Empty;

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
                && typeElement.GetString() == "text"
                && block.TryGetProperty("text", out var textElement))
            {
                text = textElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    error = "bloc texte vide dans la réponse Anthropic";
                    return false;
                }

                return true;
            }
        }

        error = "aucun bloc texte dans la réponse Anthropic";
        return false;
    }
}
