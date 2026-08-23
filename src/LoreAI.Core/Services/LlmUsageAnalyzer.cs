using System.Text.Json;
using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// S6 : agrège la consommation LLM à partir des réponses Anthropic brutes conservées pour l'audit
/// (<c>Articles.ClassificationRawResponse</c>). Défensif comme <c>ClassificationResponseParser</c> : une
/// réponse manquante, vide ou malformée (ex. panne de transport avant toute réponse HTTP) ne casse pas
/// l'agrégat, elle contribue simplement pour zéro token — jamais d'exception qui ferait échouer tout le rapport.
/// </summary>
public static class LlmUsageAnalyzer
{
    // Claude Haiku 4.5, cf. roadmap "Coût LLM et autre fournisseur" : 1 $ / 5 $ par million de tokens
    // (entrée/sortie). Les tokens de cache ont un tarif différent, non couvert par cette estimation —
    // exposés à part sur LlmUsageSummary (issue #31).
    private const decimal InputCostPerMillionUsd = 1m;
    private const decimal OutputCostPerMillionUsd = 5m;

    public static LlmUsageSummary Analyze(IReadOnlyList<string> rawResponses)
    {
        long input = 0, output = 0, cacheCreation = 0, cacheRead = 0;
        var classified = 0;

        foreach (var raw in rawResponses)
        {
            if (!TryExtractUsage(raw, out var usage))
            {
                continue;
            }

            classified++;
            input += usage.Input;
            output += usage.Output;
            cacheCreation += usage.CacheCreation;
            cacheRead += usage.CacheRead;
        }

        var estimatedCost = (input / 1_000_000m * InputCostPerMillionUsd) + (output / 1_000_000m * OutputCostPerMillionUsd);

        return new LlmUsageSummary(classified, input, output, cacheCreation, cacheRead, Math.Round(estimatedCost, 4));
    }

    private static bool TryExtractUsage(string raw, out (long Input, long Output, long CacheCreation, long CacheRead) usage)
    {
        usage = default;

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("usage", out var usageElement)
                || usageElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            usage = (
                ReadLong(usageElement, "input_tokens"),
                ReadLong(usageElement, "output_tokens"),
                ReadLong(usageElement, "cache_creation_input_tokens"),
                ReadLong(usageElement, "cache_read_input_tokens"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static long ReadLong(JsonElement usageElement, string propertyName) =>
        usageElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : 0;
}
