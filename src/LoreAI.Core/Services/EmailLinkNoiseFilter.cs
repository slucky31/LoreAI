namespace LoreAI.Core.Services;

/// <summary>
/// Filtre heuristique gratuit (lot 8, #49), avant tout appel LLM : dédup des hrefs strictement identiques,
/// exclusion des patterns triviaux qu'aucune newsletter réelle n'a jamais comme contenu (désinscription,
/// préférences, profils réseaux sociaux). Pure, testable sans I/O — tranché sur 5 newsletters réelles
/// (2026-08-26, voir roadmap lot 8) : le lien éditorial et le lien de nav partagent souvent le même domaine
/// de tracking, donc un filtre par domaine seul ne suffit pas ; celui-ci se limite au chemin/à l'ancre.
/// </summary>
public static class EmailLinkNoiseFilter
{
    // "youtube.com/@" (profil de chaîne) est exclu, mais pas "youtube.com/watch" : une vidéo peut être le
    // contenu lui-même, un profil de chaîne jamais.
    private static readonly string[] NoisyFragments =
    [
        "unsubscribe",
        "preferences",
        "linkedin.com/in/",
        "youtube.com/@",
    ];

    public static IReadOnlyList<string> Filter(IReadOnlyList<string> candidateUrls)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();

        foreach (var url in candidateUrls)
        {
            if (string.IsNullOrWhiteSpace(url) || !seen.Add(url) || IsNoise(url))
            {
                continue;
            }

            kept.Add(url);
        }

        return kept;
    }

    private static bool IsNoise(string url) =>
        NoisyFragments.Any(fragment => url.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
