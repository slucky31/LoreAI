# 0011 — EF Core remplace Dapper pour l'accès aux données

## Statut
Acceptée — remplace le choix Dapper de l'[ADR 0006](0006-stack-technique.md) ; amende l'[ADR 0009](0009-postgresql-mutualise-sur-le-pi.md) (« Ce qui ne change pas » supposait Dapper conservé).

## Contexte

La bascule PostgreSQL (D7, ADR 0009) réécrit de toute façon l'intégralité de la couche `Persistence` : fabrique de connexion, requêtes, script de schéma. L'ADR 0009 avait posé « Dapper reste l'accès aux données » comme un invariant, pour isoler le risque de la migration à un seul changement (le moteur de base).

Décision prise en session, en même temps que la PR 1 du lot 0 : ne pas s'arrêter à mi-chemin. Le raisonnement qui a justifié le *timing* de la bascule Postgres elle-même — corpus quasi vide, runner de migrations maison pas encore écrit, migrer plus tard coûterait de migrer un corpus volumineux **et** réécrire l'outillage déjà livré (voir ADR 0009, section Contexte) — s'applique identiquement au passage à EF Core. Le reporter à un lot ultérieur signifierait migrer un corpus non vide vers un nouvel accès aux données, en plus de vers un nouveau moteur.

Ce choix met de côté le principe « schéma fonctionnellement constant » que la PR 1 s'était fixé à l'origine pour isoler le risque de la bascule Postgres. Deux changements simultanés (moteur + accès aux données) plutôt qu'un seul : assumé, pas découvert après coup.

## Décision

EF Core (`Microsoft.EntityFrameworkCore` + `Npgsql.EntityFrameworkCore.PostgreSQL`) remplace Dapper **intégralement** — schéma et requêtes, pas seulement l'un des deux.

- **Migrations** : fichiers C# générés par `dotnet ef migrations add` (outillage épinglé via un manifeste d'outils local, `.config/dotnet-tools.json`), appliqués au démarrage via `Database.MigrateAsync()`. Remplace le script SQL unique embarqué et **rend obsolète le runner de migrations maison** prévu à la PR 3 du lot 0 (« lister les scripts numérotés, comparer à `SchemaVersion`, appliquer en transaction ») : `__EFMigrationsHistory`, la table de suivi native d'EF Core, joue ce rôle. La table `SchemaVersion` maison disparaît avec elle.
- **Requêtes** : `DbContext` + LINQ, via `IDbContextFactory<LoreAiDbContext>` injecté dans des repositories qui restent des singletons (un `DbContext` n'est pas thread-safe, mais une fabrique l'est). `ExecuteUpdateAsync` remplace les mises à jour en masse (ex. `MarkDigestSentAsync`) — et avec Postgres, supprime au passage le découpage manuel par lots de 500 que la limite de variables de SQLite imposait à Dapper.
- **Panne transitoire** : le comportement de l'ADR 0009 (Postgres injoignable au démarrage ≠ fatal) est repris à l'identique, porté par `PostgresSchemaGuard` — un drapeau qui ne se lève qu'après un `MigrateAsync()` réussi, retenté à chaque appel de repository tant qu'il ne l'est pas.
- **Tests** : stratégie unifiée sur `Testcontainers.PostgreSql`, **local et CI identiques** — écarte le double mécanisme (Testcontainers en local, service `postgres` natif dans le workflow GitHub Actions) initialement documenté dans l'ADR 0009. `ubuntu-latest` a déjà Docker ; Testcontainers y fonctionne nativement, sans configuration CI supplémentaire.

## Alternative écartée

**EF Core Migrations pour le schéma, Dapper conservé pour les requêtes.** Hybride techniquement possible (les deux peuvent partager la même connexion Npgsql), mais écarté : deux technologies d'accès aux données à maintenir pour un gain marginal, alors que le corpus quasi vide rend le coût de tout migrer maintenant plus faible que celui de maintenir deux mécanismes ensuite.

## Conséquences

- `Core` n'est pas touché — `IArticleRepository`/`IPollingStateRepository` sont le seam qui absorbe tout le changement, comme prévu par l'[ADR 0001](0001-architecture-generale.md).
- `ArticleEntity`/`PollingStateEntity` (classes mutables dédiées à la persistance) remplacent les `*Row` privés de Dapper — même séparation entre forme persistée et record `Core` immuable, juste portée par EF Core plutôt que par un mapping manuel post-`QueryAsync`.
- Le point 3 des « Travaux induits » de l'ADR 0009 (« Remplacer `Microsoft.Data.Sqlite` par `Npgsql`... idéalement adossé au pooling natif de Npgsql ») est satisfait par `IDbContextFactory`, qui s'appuie sur ce pooling en interne.
- Roadmap (lot 0, PR 3) : le point « Runner de migrations » est superseded, voir annotation dans `docs/roadmap.md`.
