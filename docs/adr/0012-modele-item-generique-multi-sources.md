# 0012 — Modèle `Item` générique multi-sources

## Statut
Acceptée — rouvre partiellement l'[ADR 0001](0001-architecture-generale.md) (le modèle de domaine central, pas la séparation en 3 projets, qui reste valide).

## Contexte
`RaindropItem` est aujourd'hui le modèle central du pipeline : le schéma (`ArticleEntity`), le repository, le classifieur (`IClassifier`) et les notifieurs sont tous construits autour de lui. La décision **D1** de [`docs/roadmap.md`](../roadmap.md) fait de LoreAI un hub multi-sources (Raindrop + newsletters Gmail + flux RSS, lots 7-8 à venir), et le [prérequis n°2](../roadmap.md#2-le-passage-multi-sources-doit-être-absorbé-dans-le-lot-0) de la roadmap est explicite : ce modèle générique doit exister **avant** la première indexation de masse (lot 1), sous peine de devoir migrer des milliers de lignes déjà indexées.

C'est le PR 2 du lot 0 (issue [#41](https://github.com/slucky31/LoreAI/issues/41)), qui suit la PR 1 (socle PostgreSQL/EF Core, [ADR 0009](0009-postgresql-mutualise-sur-le-pi.md), [ADR 0011](0011-ef-core-remplace-dapper.md)), déjà en production.

## Décision

- **`Item`** (`Core/Models/Item.cs`) remplace `RaindropItem` comme type central du pipeline :
  `SourceType SourceType, string SourceId, string Url, string Title, string? Excerpt, string? Note, IReadOnlyList<string> Tags, DateTimeOffset CapturedAtUtc`. Clé naturelle `(SourceType, SourceId)`.
  - `Note` et `Tags` sont **conservés** dans le modèle générique, au-delà de la liste minimale esquissée par la roadmap : ils restent nécessaires à la fusion tags/note du write-back Raindrop et au contexte donné au classifieur. Une source future sans ces notions se contente de `Tags = []`/`Note = null` — ça ne complexifie rien.
  - `Domain`, `RaindropType` et `CollectionId` (sur l'ancien `RaindropItem`) disparaissent : recherche dans le code, ils n'étaient jamais lus ailleurs qu'à l'écriture en base, puis re-sérialisés sans usage réel. `Domain` redevient une valeur calculée à la volée (`Uri.TryCreate(item.Url, ...).Host`) plutôt que stockée.
- **`RaindropItem` est supprimé**, pas conservé comme DTO d'adaptateur séparé : le DTO de désérialisation JSON existant (`RaindropDto`, `Infrastructure/Raindrop/Dto`) joue déjà ce rôle. Ajouter un `RaindropItem` intermédiaire entre `RaindropDto` et `Item` aurait été une couche sans valeur ajoutée, puisque plus rien en aval n'a besoin de champs spécifiquement Raindrop après le mapping.
- **`ISourceIngester`** (`Core/Interfaces`) : `SourceType SourceType { get; }` + `Task<IReadOnlyList<Item>> GetNewItemsAsync(PollingState lastState, CancellationToken ct)`. `IRaindropClient : ISourceIngester`, avec en plus ses deux membres propres à Raindrop (`GetTaxonomyAsync`, `UpdateRaindropAsync`) qui ne montent pas dans l'interface générique — la taxonomie apprise et le write-back restent des concepts Raindrop, pas multi-sources.
- **`PollingState`** devient scopé par source : `SourceType SourceType, string? LastSourceItemId, DateTimeOffset? LastCreatedUtc, DateTimeOffset UpdatedAtUtc`, avec `PollingState.Initial(SourceType)` en fabrique. `IPollingStateRepository.GetAsync` prend désormais un `SourceType`. Persisté par une ligne par source (clé primaire = `SourceType`) au lieu de la ligne unique `Id = 1` d'origine.
- **`IClassifier.ClassifyAsync`**, **`IImmediateNotifier.NotifyAsync`**, **`IArticleRepository.UpsertAsync`** et **`ClassifiedArticle.Item`** prennent/portent désormais un `Item`.
- **Le write-back reste strictement spécifique à Raindrop** : `UnsortedClassificationJob` reste câblé directement sur `IRaindropClient`, pas sur une boucle générique `IEnumerable<ISourceIngester>` — prématuré tant qu'une seule source existe (voir « Sur-ingénierie » dans les risques de la roadmap). Le job retrouve l'id Raindrop numérique via `long.Parse(item.SourceId)` pour appeler `UpdateRaindropAsync`/`RecordWriteBackAsync`/`MarkDiscordNotifiedAsync`.
- **`ArticleEntity` reste identifiée par l'id Raindrop numérique** (`long Id`) : pas de colonnes `SourceType`/`SourceId` génériques dans ce lot. Généraliser la clé de la table `Articles` n'a de sens que lorsqu'une deuxième source existe réellement (lot 7/8) ; l'anticiper maintenant serait spéculatif. Choix assumé, distinct de l'élargissement d'`IArticleRepository` (`GetByIdAsync`, etc.) prévu en **PR 3** du même lot.

## Conséquences

- Migration EF Core (`AddMultiSourceItemModel`) : renommage `Articles.Link` → `Url`, `RaindropCreatedUtc` → `CapturedAtUtc` ; suppression de `CollectionId`/`Domain`/`RaindropType`/`RaindropLastUpdateUtc` (jamais lues) ; `PollingStates` passe d'une ligne unique (`Id = 1`) à une clé primaire `SourceType`, `LastRaindropId` (bigint) devenant `LastSourceItemId` (text). La transformation passe par `RenameColumn`/`ALTER COLUMN ... TYPE ... USING ...` plutôt que par un drop/recreate : la ligne `PollingStates` existante en production (`mcm8`) est convertie, pas réinitialisée — un curseur perdu rejouerait tout l'historique de « Non trié » au cycle suivant (cf. « First-run caveat », `CLAUDE.md`).
- Un futur `ISourceIngester` (Feed, Newsletter) n'aura ni taxonomie ni write-back : ces items seront classifiés une fois les lots 7/8 atteints, mais jamais écrits nulle part automatiquement — contrainte déjà actée par D1/roadmap.
- Perte mineure et assumée de fidélité de prompt : `Domain` n'est plus stocké mais recalculé à la volée — comportement inchangé pour le prompt de classification, qui continue à le recevoir.
- `ADR 0001` reste valable dans son ensemble (3 projets, pas de Clean Architecture/CQRS/MediatR) ; ce qu'il documentait implicitement sur `RaindropItem` comme modèle central est corrigé par cet ADR.
