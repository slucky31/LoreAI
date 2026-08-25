using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using LoreAI.Core.Enums;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Classification;

/// <summary>
/// Valide défensivement la sortie du LLM même quand elle est censée être contrainte par schéma
/// (tool-use forcé côté Anthropic) : enums insensibles à la casse, fences ```json résiduels tolérés.
/// </summary>
public static class ClassificationResponseParser
{
    /// <summary>
    /// Les tags sont la seule sortie libre du modèle réellement écrite dans Raindrop : le schéma d'outil
    /// contraint la collection à un titre existant et la raison à 200 caractères, mais pas eux. Un extrait
    /// de page hostile pourrait donc tenter d'y faire écrire n'importe quoi (cf. F-11). D'où ces plafonds.
    /// </summary>
    private const int MaxTagLength = 50;

    private const int MaxTags = 10;

    private const int MaxToolNameLength = 100;
    private const int MaxToolCategoryLength = 60;
    private const int MaxToolUrlLength = 300;

    /// <summary>
    /// Variante sans exception, destinée à l'appelant nominal : une sortie de modèle invalide est un
    /// résultat attendu, pas un incident. Cela permet à <c>AnthropicClassifier</c> de ne plus envelopper
    /// tout son corps dans un <c>catch (Exception)</c>, qui masquait aussi bien un JSON malformé qu'un
    /// bug de programmation.
    /// </summary>
    public static bool TryParse(
        string toolInputJson,
        string model,
        string rawResponse,
        [NotNullWhen(true)] out ClassificationResult? result,
        [NotNullWhen(false)] out string? error)
    {
        try
        {
            result = Parse(toolInputJson, model, rawResponse);
            error = null;
            return true;
        }
        catch (ClassificationParseException ex)
        {
            result = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Lève <see cref="ClassificationParseException"/> ; préférer <see cref="TryParse"/> côté appelant.</summary>
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
            var summary = root.TryGetProperty("summary", out var summaryElement)
                ? summaryElement.GetString() ?? string.Empty
                : string.Empty;
            var toolName = SanitizeOptionalText(TryParseOptionalNullableString(root, "toolName"), MaxToolNameLength);
            var toolCategory = SanitizeOptionalText(TryParseOptionalNullableString(root, "toolCategory"), MaxToolCategoryLength);
            var toolUrl = SanitizeOptionalText(TryParseOptionalNullableString(root, "toolUrl"), MaxToolUrlLength);

            return new ClassificationResult(suggestedCollection, tags, action, priority, reason, summary, model, rawResponse)
            {
                ToolName = toolName,
                ToolCategory = toolCategory,
                ToolUrl = toolUrl,
            };
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

    /// <summary>
    /// Comme <c>summary</c> (lot 4) : le schéma marque le champ <c>required</c> mais rien ne garantit que le
    /// modèle l'honore, et les fixtures pré-lot-5 ne le portent pas — champ absent ou <c>null</c> traités
    /// identiquement, contrairement à <see cref="ParseNullableString"/> qui exige la présence du champ.
    /// </summary>
    private static string? TryParseOptionalNullableString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static List<string> ParseStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            throw new ClassificationParseException($"Champ '{propertyName}' manquant ou invalide.");
        }

        return element.EnumerateArray()
            .Select(e => e.GetString())
            .Select(SanitizeTag)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTags)
            .ToList();
    }

    /// <summary>
    /// Aplatit les blancs (un tag ne tient que sur une ligne), retire les caractères de contrôle et tronque.
    /// Rien de ce que renvoie le modèle ne doit pouvoir se déverser tel quel dans les données de l'utilisateur.
    /// </summary>
    private static string SanitizeTag(string? raw) => SanitizeFreeText(raw, MaxTagLength) ?? string.Empty;

    /// <summary>Même traitement que <see cref="SanitizeTag"/> (F-11) pour toolName/toolCategory (S7, lot 5), mais <c>null</c> plutôt que vide quand rien n'exploitable ne subsiste.</summary>
    private static string? SanitizeOptionalText(string? raw, int maxLength) => SanitizeFreeText(raw, maxLength);

    private static string? SanitizeFreeText(string? raw, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var collapsed = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var printable = new string(collapsed.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (printable.Length == 0)
        {
            return null;
        }

        return printable.Length <= maxLength ? printable : printable[..maxLength].TrimEnd();
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
