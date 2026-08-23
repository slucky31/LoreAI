using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// N1 : regroupe les items dont l'URL normalisée coïncide. Pure, testable sans I/O — zéro LLM, comme
/// demandé par la roadmap. Ne fusionne jamais rien : c'est un rapport, l'utilisateur décide.
/// </summary>
public static class DuplicateUrlDetector
{
    public static IReadOnlyList<DuplicateUrlGroup> Detect(IReadOnlyList<LibraryItemSummary> items)
    {
        var groups = new Dictionary<string, List<LibraryItemSummary>>();

        foreach (var item in items)
        {
            var normalized = Normalize(item.Url);
            if (!groups.TryGetValue(normalized, out var members))
            {
                members = [];
                groups[normalized] = members;
            }

            members.Add(item);
        }

        return groups
            .Where(g => g.Value.Count > 1)
            .OrderByDescending(g => g.Value.Count)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new DuplicateUrlGroup(
                g.Key,
                g.Value.Select(i => new DuplicateLink(i.Id, i.Title, i.Url)).ToList()))
            .ToList();
    }

    /// <summary>
    /// Normalisation volontairement limitée à ce que la roadmap demande pour N1 : paramètres <c>utm_*</c>,
    /// fragment, préfixe <c>www.</c>, slash final. Deux URLs qui ne diffèrent que sur ces quatre points sont,
    /// en pratique, la même page bookmarkée deux fois — au-delà, on risquerait des faux positifs.
    /// </summary>
    private static string Normalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url.Trim().ToLowerInvariant();
        }

        var host = StripWww(uri.Host);
        var path = uri.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path[..^1];
        }

        // uri.Query/AbsolutePath excluent déjà le fragment (uri.Fragment est séparé) : rien à faire pour le "#".
        var query = StripUtmParameters(uri.Query);

        return $"{uri.Scheme.ToLowerInvariant()}://{host.ToLowerInvariant()}{path}{query}";
    }

    private static string StripWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private static string StripUtmParameters(string query)
    {
        if (query.Length <= 1)
        {
            return string.Empty;
        }

        var kept = query[1..]
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !pair.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return kept.Count == 0 ? string.Empty : "?" + string.Join('&', kept);
    }
}
