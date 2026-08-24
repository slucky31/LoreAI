using LoreAI.Core.Models;

namespace LoreAI.Core.Interfaces;

/// <summary>
/// Lecture seule du corpus complet (table <c>LibraryItems</c>, lot 1 — <c>Articles</c> n'est qu'un
/// sous-ensemble passé par le pipeline de classification) pour le serveur MCP (lot 3, ADR 0014).
/// Contrairement aux autres repositories, une implémentation ne doit <b>jamais</b> appeler
/// <c>PostgresSchemaGuard</c> : le rôle <c>loreai_ro</c> (<c>GRANT SELECT</c>, ADR 0009) n'a pas les
/// privilèges de migration, et ce n'est de toute façon pas sa responsabilité — c'est celle du Worker,
/// seul propriétaire de la base.
/// </summary>
public interface ICorpusQueryRepository
{
    Task<LibraryItemSummary?> GetByIdAsync(long id, CancellationToken cancellationToken);

    Task<IReadOnlyList<LibraryItemSummary>> GetRecentAsync(int count, CancellationToken cancellationToken);

    /// <summary>Recherche naïve par sous-chaîne (titre/URL) — pas de recherche plein texte (Q2, roadmap).</summary>
    Task<IReadOnlyList<LibraryItemSummary>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    Task<CorpusStats> GetStatsAsync(CancellationToken cancellationToken);
}
