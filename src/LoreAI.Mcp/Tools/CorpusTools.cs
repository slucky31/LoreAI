using System.ComponentModel;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LoreAI.Mcp.Tools;

/// <summary>
/// Façades fines sur <see cref="ICorpusQueryRepository"/> (lot 3, ADR 0014) — aucune logique métier ici,
/// seulement la traduction protocole ↔ repository. Tous les outils sont en lecture seule
/// (<c>ReadOnly = true</c>) : c'est le rôle Postgres <c>loreai_ro</c>, pas cet attribut, qui l'impose
/// réellement, mais l'exposer aide les clients MCP à comprendre l'absence d'effet de bord.
/// </summary>
[McpServerToolType]
public sealed class CorpusTools
{
    private const int DefaultRecentCount = 20;
    private const int MaxRecentCount = 200;
    private const int DefaultSearchLimit = 20;
    private const int MaxSearchLimit = 100;

    private readonly ICorpusQueryRepository _repository;

    public CorpusTools(ICorpusQueryRepository repository)
    {
        _repository = repository;
    }

    [McpServerTool(Name = "get_item", ReadOnly = true), Description("Récupère un item du corpus par son identifiant Raindrop.")]
    public async Task<LibraryItemSummary> GetItem(
        [Description("Identifiant Raindrop de l'item.")] long id,
        CancellationToken cancellationToken)
    {
        return await _repository.GetByIdAsync(id, cancellationToken)
            ?? throw new McpException($"Aucun item {id} dans le corpus indexé.");
    }

    [McpServerTool(Name = "list_recent", ReadOnly = true), Description("Liste les items les plus récemment capturés dans le corpus, du plus récent au plus ancien.")]
    public async Task<IReadOnlyList<LibraryItemSummary>> ListRecent(
        [Description("Nombre d'items à retourner (défaut 20, max 200).")] int count,
        CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(count <= 0 ? DefaultRecentCount : count, 1, MaxRecentCount);
        return await _repository.GetRecentAsync(clamped, cancellationToken);
    }

    [McpServerTool(Name = "search_items", ReadOnly = true), Description("Recherche plein texte française (titre + extrait) des items du corpus, classés par pertinence.")]
    public async Task<IReadOnlyList<LibraryItemSummary>> SearchItems(
        [Description("Terme recherché.")] string query,
        [Description("Nombre maximum de résultats (défaut 20, max 100).")] int limit,
        CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(limit <= 0 ? DefaultSearchLimit : limit, 1, MaxSearchLimit);
        return await _repository.SearchAsync(query, clamped, cancellationToken);
    }

    [McpServerTool(Name = "find_similar", ReadOnly = true), Description("Articles liés à un item du corpus (recherche plein texte sur son titre), du plus au moins pertinent.")]
    public async Task<IReadOnlyList<LibraryItemSummary>> FindSimilar(
        [Description("Identifiant Raindrop de l'item source.")] long id,
        [Description("Nombre maximum de résultats (défaut 20, max 100).")] int limit,
        CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(limit <= 0 ? DefaultSearchLimit : limit, 1, MaxSearchLimit);
        return await _repository.FindSimilarAsync(id, clamped, cancellationToken);
    }

    [McpServerTool(Name = "stats", ReadOnly = true), Description("Statistiques globales du corpus indexé (volumétrie, items importants/cassés, fraîcheur de l'index).")]
    public async Task<CorpusStats> Stats(CancellationToken cancellationToken)
    {
        return await _repository.GetStatsAsync(cancellationToken);
    }

    [McpServerTool(Name = "list_tools", ReadOnly = true), Description("Liste les outils MCP prévus pour ce serveur (issue #44) et leur statut d'implémentation.")]
    public IReadOnlyList<McpToolStatus> ListTools()
    {
        return
        [
            new McpToolStatus("get_item", "implémenté"),
            new McpToolStatus("list_recent", "implémenté"),
            new McpToolStatus("search_items", "implémenté (plein texte — Q2, lot 5)"),
            new McpToolStatus("stats", "implémenté"),
            new McpToolStatus("list_tools", "implémenté"),
            new McpToolStatus("find_similar", "implémenté (S5, lot 5)"),
            new McpToolStatus("reading_queue", "non implémenté — dépend du scoring du lot 6 (L1)"),
        ];
    }
}

public sealed record McpToolStatus(string Name, string Status);
