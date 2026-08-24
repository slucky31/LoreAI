using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Façade de lecture seule pour le serveur MCP (lot 3, ADR 0014), sur <c>LibraryItems</c> (le corpus
/// complet, lot 1), <c>Articles</c> (sous-ensemble classifié) et <c>Tools</c> (base d'outils, S7, lot 5).
/// Contrairement aux autres repositories, une implémentation ne doit <b>jamais</b> appeler
/// <c>PostgresSchemaGuard</c> : le rôle <c>loreai_ro</c> (<c>GRANT SELECT</c>, ADR 0009) n'a pas les
/// privilèges de migration, et ce n'est de toute façon pas sa responsabilité — c'est celle du Worker,
/// seul propriétaire de la base.
/// </summary>
public interface ICorpusQueryRepository
{
    Task<LibraryItemSummary?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LibraryItemSummary>> GetRecentAsync(int count, CancellationToken cancellationToken);

    /// <summary>Recherche plein texte (français, <c>tsvector</c>/GIN — Q2, lot 5) sur le titre et l'extrait.</summary>
    Task<IReadOnlyList<LibraryItemSummary>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Articles liés (S5, lot 5) : ré-utilise la recherche plein texte de <see cref="SearchAsync"/> avec une
    /// requête dérivée du titre de l'item source, plutôt que des embeddings — recherche plein texte d'abord,
    /// cf. l'arbitrage du roadmap sur S5. Liste vide si l'id source est inconnu.
    /// </summary>
    Task<IReadOnlyList<LibraryItemSummary>> FindSimilarAsync(long id, int limit, CancellationToken cancellationToken);

    Task<CorpusStats> GetStatsAsync(CancellationToken cancellationToken);

    /// <summary>Catalogue de la base d'outils (S7, lot 5), pour l'outil MCP <c>catalog_tools</c>.</summary>
    Task<IReadOnlyList<ToolSummary>> GetToolsAsync(CancellationToken cancellationToken);

    /// <summary>Fiche complète d'un outil par nom (S7, lot 5), avec ses articles liés — <c>null</c> si inconnu.</summary>
    Task<ToolCard?> GetToolByNameAsync(string name, CancellationToken cancellationToken);
}
