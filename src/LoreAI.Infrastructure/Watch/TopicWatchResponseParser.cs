using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using LoreAI.Core.Models;
using LoreAI.Infrastructure.Classification;

namespace LoreAI.Infrastructure.Watch;

/// <summary>Valide défensivement la sortie de l'évaluation de veille (lot 9, #50), même patron que <see cref="ClassificationResponseParser"/>.</summary>
public static class TopicWatchResponseParser
{
    private const int MaxReasonLength = 200;
    private const int MaxTopicNameLength = 200;

    public static bool TryParse(
        string toolInputJson,
        string model,
        string rawResponse,
        [NotNullWhen(true)] out WatchEvaluation? result,
        [NotNullWhen(false)] out string? error)
    {
        try
        {
            result = Parse(toolInputJson, model, rawResponse);
            error = null;
            return true;
        }
        catch (TopicWatchParseException ex)
        {
            result = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Lève <see cref="TopicWatchParseException"/> ; préférer <see cref="TryParse"/> côté appelant.</summary>
    public static WatchEvaluation Parse(string toolInputJson, string model, string rawResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(LlmResponseTextSanitizer.StripCodeFences(toolInputJson));
            var root = document.RootElement;

            if (!root.TryGetProperty("isRelevant", out var isRelevantElement)
                || (isRelevantElement.ValueKind != JsonValueKind.True && isRelevantElement.ValueKind != JsonValueKind.False))
            {
                throw new TopicWatchParseException("Champ 'isRelevant' manquant ou invalide.");
            }

            if (!root.TryGetProperty("isNew", out var isNewElement)
                || (isNewElement.ValueKind != JsonValueKind.True && isNewElement.ValueKind != JsonValueKind.False))
            {
                throw new TopicWatchParseException("Champ 'isNew' manquant ou invalide.");
            }

            var isRelevant = isRelevantElement.GetBoolean();
            var isNew = isNewElement.GetBoolean();

            var matchedTopic = root.TryGetProperty("matchedTopic", out var matchedTopicElement) && matchedTopicElement.ValueKind == JsonValueKind.String
                ? LlmResponseTextSanitizer.SanitizeFreeText(matchedTopicElement.GetString(), MaxTopicNameLength)
                : null;

            var reason = root.TryGetProperty("reason", out var reasonElement) && reasonElement.ValueKind == JsonValueKind.String
                ? LlmResponseTextSanitizer.SanitizeFreeText(reasonElement.GetString(), MaxReasonLength)
                : null;

            return new WatchEvaluation(
                isRelevant,
                // Une entrée non pertinente n'est jamais nouvelle : cohérence forcée côté code, pas
                // seulement demandée dans le prompt, au cas où le modèle contredirait l'instruction.
                isRelevant && isNew,
                isRelevant ? matchedTopic : null,
                reason ?? string.Empty,
                model,
                rawResponse);
        }
        catch (Exception ex) when (ex is not TopicWatchParseException)
        {
            throw new TopicWatchParseException($"Impossible de parser la réponse d'évaluation de veille : {ex.Message}", ex);
        }
    }
}
