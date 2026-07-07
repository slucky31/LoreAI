using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Classification;

/// <summary>Construit le prompt et le schéma d'outil envoyés au LLM — pure, testable sans appel réseau.</summary>
public static class ClassificationPromptBuilder
{
    private const int MaxExcerptLength = 2000;

    public const string ToolName = "classify";

    public const string SystemPrompt = """
        Tu es un assistant qui aide un développeur .NET à trier les articles/liens qu'il collecte.
        Il s'intéresse particulièrement à : l'écosystème .NET, les outils IA de code (notamment Claude d'Anthropic), et les contenus de formation.
        Pour chaque lien fourni, classe-le selon :
        - category : DotNet (contenu lié à .NET/C#), ClaudeIA (outils IA de code, notamment Claude/Anthropic), Formation (cours, tutoriels structurés), Autre (tout le reste).
        - action : ALire si c'est un contenu long-form à lire (article, doc, blog, annonce) ; ATester si c'est un outil, une librairie, un repo ou un produit à essayer concrètement ; Reference si c'est à garder sous la main sans action immédiate.
        - priority : Haute, Moyenne ou Basse, selon la pertinence estimée pour ce développeur.
        - reason : une justification courte en français (200 caractères maximum).
        Utilise impérativement l'outil "classify" pour renvoyer ta réponse.
        """;

    public static string BuildToolInputSchemaJson() => """
        {
          "type": "object",
          "properties": {
            "category": { "type": "string", "enum": ["DotNet", "ClaudeIA", "Formation", "Autre"] },
            "action": { "type": "string", "enum": ["ALire", "ATester", "Reference"] },
            "priority": { "type": "string", "enum": ["Haute", "Moyenne", "Basse"] },
            "reason": { "type": "string", "maxLength": 200 }
          },
          "required": ["category", "action", "priority", "reason"]
        }
        """;

    public static string BuildUserMessage(RaindropItem item)
    {
        var excerpt = Truncate(item.Excerpt, MaxExcerptLength);
        var tags = item.Tags.Count > 0 ? string.Join(", ", item.Tags) : "(aucun)";

        return $"""
            Titre : {item.Title}
            Lien : {item.Link}
            Domaine : {item.Domain ?? "(inconnu)"}
            Tags existants : {tags}
            Extrait : {excerpt ?? "(aucun extrait)"}
            Note personnelle : {item.Note ?? "(aucune)"}
            """;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength] + "…";
}
