using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Classification;

/// <summary>
/// Valide défensivement la sortie de l'extraction de liens (lot 8, #49), même patron que
/// <see cref="ClassificationResponseParser"/>. Le modèle désigne un lien par son index dans la liste
/// candidate (jamais en recopiant l'URL — évite de faire porter au budget de tokens le coût de recopier des
/// URLs de tracking parfois très longues, cause d'une troncature réelle observée le 2026-08-29) : un index
/// hors bornes est rejeté, ce qui offre la même garantie qu'avant contre une URL inventée.
/// </summary>
public static class EmailLinkExtractionResponseParser
{
    private const int MaxLinks = 20;
    private const int MaxTitleLength = 200;

    public static bool TryParse(
        string toolInputJson,
        IReadOnlyList<string> candidateUrls,
        [NotNullWhen(true)] out IReadOnlyList<ExtractedLink>? result,
        [NotNullWhen(false)] out string? error)
    {
        try
        {
            result = Parse(toolInputJson, candidateUrls);
            error = null;
            return true;
        }
        catch (EmailLinkExtractionParseException ex)
        {
            result = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Lève <see cref="EmailLinkExtractionParseException"/> ; préférer <see cref="TryParse"/> côté appelant.</summary>
    public static IReadOnlyList<ExtractedLink> Parse(string toolInputJson, IReadOnlyList<string> candidateUrls)
    {
        try
        {
            using var document = JsonDocument.Parse(LlmResponseTextSanitizer.StripCodeFences(toolInputJson));
            var root = document.RootElement;

            if (!root.TryGetProperty("links", out var linksElement) || linksElement.ValueKind != JsonValueKind.Array)
            {
                throw new EmailLinkExtractionParseException("Champ 'links' manquant ou invalide.");
            }

            return linksElement.EnumerateArray()
                .Select(element => ParseLink(element, candidateUrls))
                .Where(link => link is not null)
                .Cast<ExtractedLink>()
                .DistinctBy(link => link.Url, StringComparer.Ordinal)
                .Take(MaxLinks)
                .ToList();
        }
        catch (Exception ex) when (ex is not EmailLinkExtractionParseException)
        {
            throw new EmailLinkExtractionParseException($"Impossible de parser la réponse d'extraction de liens : {ex.Message}", ex);
        }
    }

    private static ExtractedLink? ParseLink(JsonElement element, IReadOnlyList<string> candidateUrls)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("index", out var indexElement)
            || indexElement.ValueKind != JsonValueKind.Number
            || !indexElement.TryGetInt32(out var index)
            || index < 0 || index >= candidateUrls.Count)
        {
            return null;
        }

        var url = candidateUrls[index];

        var title = element.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
            ? titleElement.GetString()
            : null;

        return new ExtractedLink(url, LlmResponseTextSanitizer.SanitizeFreeText(title, MaxTitleLength) ?? url);
    }
}
