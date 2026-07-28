using System.Text.Json;
using RaindropAI.Core.Enums;
using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Classification;

/// <summary>
/// Valide défensivement la sortie du LLM même quand elle est censée être contrainte par schéma
/// (tool-use forcé côté Anthropic) : enums insensibles à la casse, fences ```json résiduels tolérés.
/// </summary>
public static class ClassificationResponseParser
{
    public static ClassificationResult Parse(string toolInputJson, string model, string rawResponse)
    {
        try
        {
            using var document = JsonDocument.Parse(StripCodeFences(toolInputJson));
            var root = document.RootElement;

            var suggestedCollection = ParseNullableString(root, "suggestedCollection");
            var tags = ParseStringArray(root, "tags");
            var action = ParseEnum<RecommendedAction>(root, "action");
            var priority = ParseEnum<Priority>(root, "priority");
            var reason = root.TryGetProperty("reason", out var reasonElement)
                ? reasonElement.GetString() ?? string.Empty
                : string.Empty;

            return new ClassificationResult(suggestedCollection, tags, action, priority, reason, model, rawResponse);
        }
        catch (Exception ex) when (ex is not ClassificationParseException)
        {
            throw new ClassificationParseException($"Impossible de parser la réponse de classification : {ex.Message}", ex);
        }
    }

    private static string? ParseNullableString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element))
        {
            throw new ClassificationParseException($"Champ '{propertyName}' manquant.");
        }

        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            _ => throw new ClassificationParseException($"Champ '{propertyName}' de type invalide."),
        };
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new ClassificationParseException($"Champ '{propertyName}' manquant ou invalide.");
        }

        return element.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToList();
    }

    private static TEnum ParseEnum<TEnum>(JsonElement root, string propertyName) where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            throw new ClassificationParseException($"Champ '{propertyName}' manquant ou invalide.");
        }

        var raw = element.GetString()!;
        if (!Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value))
        {
            throw new ClassificationParseException($"Valeur '{raw}' invalide pour le champ '{propertyName}'.");
        }

        return value;
    }

    private static string StripCodeFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        trimmed = firstNewLine >= 0 ? trimmed[(firstNewLine + 1)..] : trimmed;

        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        if (lastFence >= 0)
        {
            trimmed = trimmed[..lastFence];
        }

        return trimmed.Trim();
    }
}
