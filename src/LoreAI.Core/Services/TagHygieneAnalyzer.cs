using System.Text;
using LoreAI.Core.Models;

namespace LoreAI.Core.Services;

/// <summary>
/// N2 : grappes de tags à l'orthographe proche, et tags utilisés une seule fois. Zéro LLM — une distance
/// de Levenshtein sur la taxonomie déjà apprise (<see cref="RaindropTag"/>) suffit pour les variantes de
/// séparateurs (<c>dotnet</c>/<c>dot-net</c>) ; elle ne peut pas rapprocher deux tags qui se ressemblent
/// seulement à l'oreille (<c>.net</c>/<c>dotnet</c>) — hors de portée d'un algorithme purement textuel,
/// et un vrai rapprochement sémantique demanderait un LLM. Rapport seul, jamais de fusion automatique.
/// </summary>
public static class TagHygieneAnalyzer
{
    private const int MaxLevenshteinDistance = 1;

    public static TagHygieneResult Analyze(IReadOnlyList<RaindropTag> tags)
    {
        var singleUse = tags
            .Where(t => t.Count == 1)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TagHygieneResult(BuildClusters(tags), singleUse);
    }

    private static List<TagCluster> BuildClusters(IReadOnlyList<RaindropTag> tags)
    {
        var normalized = tags.Select(t => (t.Name, Normalized: Normalize(t.Name))).ToList();
        var clustered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var clusters = new List<TagCluster>();

        for (var i = 0; i < normalized.Count; i++)
        {
            var (name, key) = normalized[i];
            if (clustered.Contains(name))
            {
                continue;
            }

            var members = new List<string> { name };
            for (var j = i + 1; j < normalized.Count; j++)
            {
                var (otherName, otherKey) = normalized[j];
                if (!clustered.Contains(otherName) && Levenshtein(key, otherKey) <= MaxLevenshteinDistance)
                {
                    members.Add(otherName);
                }
            }

            if (members.Count <= 1)
            {
                continue;
            }

            foreach (var member in members)
            {
                clustered.Add(member);
            }

            clusters.Add(new TagCluster(members.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()));
        }

        return clusters;
    }

    /// <summary>Ne garde que lettres/chiffres, en minuscules : gomme les variantes de séparateur avant comparaison.</summary>
    private static string Normalize(string tag)
    {
        var builder = new StringBuilder(tag.Length);
        foreach (var ch in tag)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
