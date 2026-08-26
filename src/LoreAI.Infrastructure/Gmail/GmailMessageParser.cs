using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LoreAI.Infrastructure.Gmail;

/// <summary>
/// Décode le payload MIME d'un message Gmail (<c>users.messages.get?format=full</c>) — pure, testable par
/// fixtures JSON, sans appel réseau (lot 8, #49). Privilégie la partie <c>text/plain</c> d'un
/// <c>multipart/alternative</c> : sur 5 newsletters réelles analysées, elle est déjà lisible, en ordre de
/// lecture, avec les URLs inlinées (<c>texte ( https://... )</c>) — nettement plus robuste que d'analyser le
/// DOM HTML de templates tous différents. Fallback HTML→texte brut (strip tags, décode entités) seulement
/// si le mail est HTML-only ; pas d'extracteur readability comme S1, la fidélité de mise en page n'a pas à
/// être parfaite ici.
/// </summary>
public static class GmailMessageParser
{
    // Capture aussi bien les cibles href="..." d'un mail HTML-only que les URLs inlinées d'un mail
    // text/plain — un même regex suffit, aucun des deux cas n'a besoin d'un vrai parseur.
    private static readonly Regex HrefRegex = new("href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BareUrlRegex = new("https?://[^\\s\"'<>\\)]+", RegexOptions.Compiled);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    public static ParsedEmailMessage Parse(JsonElement messagePayload)
    {
        var subject = ExtractHeader(messagePayload, "Subject") ?? string.Empty;

        if (TryFindPartData(messagePayload, "text/plain", out var plainData))
        {
            var text = DecodeBase64Url(plainData);
            return new ParsedEmailMessage(subject, text, ExtractCandidateUrls(text));
        }

        if (TryFindPartData(messagePayload, "text/html", out var htmlData))
        {
            var html = DecodeBase64Url(htmlData);
            // Les URLs vivent dans les attributs href, perdus par le strip de balises : extraites d'abord.
            var candidateUrls = ExtractCandidateUrls(html);
            return new ParsedEmailMessage(subject, StripHtml(html), candidateUrls);
        }

        return new ParsedEmailMessage(subject, string.Empty, []);
    }

    public static IReadOnlyList<string> ExtractCandidateUrls(string rawContent)
    {
        var urls = new List<string>();
        urls.AddRange(HrefRegex.Matches(rawContent).Select(m => m.Groups[1].Value));
        urls.AddRange(BareUrlRegex.Matches(rawContent).Select(m => m.Value.TrimEnd('.', ',', ';', ')')));

        return urls
            .Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string StripHtml(string html) =>
        System.Net.WebUtility.HtmlDecode(HtmlTagRegex.Replace(html, " "));

    private static bool TryFindPartData(JsonElement part, string mimeType, out string data)
    {
        if (part.ValueKind == JsonValueKind.Object
            && part.TryGetProperty("mimeType", out var mimeTypeElement)
            && string.Equals(mimeTypeElement.GetString(), mimeType, StringComparison.OrdinalIgnoreCase)
            && part.TryGetProperty("body", out var bodyElement)
            && bodyElement.TryGetProperty("data", out var dataElement)
            && dataElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(dataElement.GetString()))
        {
            data = dataElement.GetString()!;
            return true;
        }

        if (part.ValueKind == JsonValueKind.Object
            && part.TryGetProperty("parts", out var partsElement)
            && partsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in partsElement.EnumerateArray())
            {
                if (TryFindPartData(child, mimeType, out data))
                {
                    return true;
                }
            }
        }

        data = string.Empty;
        return false;
    }

    private static string? ExtractHeader(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var header in headers.EnumerateArray())
        {
            if (header.TryGetProperty("name", out var nameElement)
                && string.Equals(nameElement.GetString(), name, StringComparison.OrdinalIgnoreCase)
                && header.TryGetProperty("value", out var valueElement))
            {
                return valueElement.GetString();
            }
        }

        return null;
    }

    /// <summary>Gmail encode les corps de message en base64url (RFC 4648 §5), pas en base64 standard.</summary>
    private static string DecodeBase64Url(string base64Url)
    {
        var padded = base64Url.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded,
        };

        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}

/// <summary>Résultat de <see cref="GmailMessageParser.Parse"/> : sujet, corps texte, et URLs candidates avant tout filtre heuristique.</summary>
public sealed record ParsedEmailMessage(string Subject, string Body, IReadOnlyList<string> CandidateUrls);
