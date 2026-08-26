using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using LoreAI.Core.Models;

namespace LoreAI.Infrastructure.Classification;

/// <summary>
/// Valide défensivement la sortie de l'extraction de liens (lot 8, #49), même patron que
/// <see cref="ClassificationResponseParser"/>. Garde supplémentaire propre à ce parseur : une URL renvoyée
/// par le modèle qui ne figure pas mot pour mot dans les URLs candidates fournies est rejetée — le modèle
/// ne doit jamais pouvoir faire écrire une URL inventée ou légèrement modifiée.
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

            var candidateSet = new HashSet<string>(candidateUrls, StringComparer.Ordinal);

            return linksElement.EnumerateArray()
                .Select(ParseLink)
                .Where(link => link is not null && candidateSet.Contains(link.Url))
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

    private static ExtractedLink? ParseLink(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("url", out var urlElement)
            || urlElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var url = urlElement.GetString();
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var title = element.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
            ? titleElement.GetString()
            : null;

        return new ExtractedLink(url, LlmResponseTextSanitizer.SanitizeFreeText(title, MaxTitleLength) ?? url);
    }
}
