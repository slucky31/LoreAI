# Roadmap — exploiter le contenu classé

## Pourquoi ce document

RaindropAI ne fait aujourd'hui que la moitié du travail : il capte les nouveaux articles de « Non trié », les classe, les range, puis notifie (Discord immédiat, digest mail quotidien). Le résultat n'est qu'un **flux sortant** : une fois l'article rangé et l'email envoyé, la base ne sert plus à rien. `ArticleRepository` ne contient d'ailleurs qu'un seul `SELECT` (`GetUnsentDigestItemsAsync`) — tout le reste du schéma est en écriture seule.

Ce document cartographie les scénarios qui transforment cette base en **actif exploitable**, sur trois axes :

1. **Synthétiser** — extraire de la valeur agrégée, pas seulement ranger.
2. **Nettoyer** — doublons, liens morts, tags redondants, articles périmés.
3. **Lire** — pousser la bonne chose au bon moment, et boucler la boucle.

Deux canaux de restitution sont retenus : un **serveur MCP** pour l'interrogation ad-hoc depuis Claude Code, et les **notifications Discord/mail enrichies** pour le push périodique. Pas d'interface web.

C'est une carte, pas un engagement de livraison : les lots sont indépendants et peuvent être pris dans un autre ordre, à condition de respecter les dépendances signalées.

## Prérequis oublié : le corpus est presque vide

Point le plus important de tout ce document.

La table `Articles` ne contient **que ce qui est passé par « Non trié » depuis le premier démarrage**. Toute la bibliothèque déjà triée — des années de veille — est invisible pour l'outil.

Or synthèse, similarité, détection de doublons et statistiques n'ont de valeur **qu'à l'échelle du corpus complet**. Sans indexation préalable, la plupart des scénarios ci-dessous tournent sur quelques dizaines de lignes et ne produisent rien d'intéressant.

L'indexation en lecture seule de toute la bibliothèque (lot 1) est donc le vrai prérequis fonctionnel. Elle ne viole pas l'invariant « on ne touche jamais hors Non trié » : c'est un `GET /raindrops/0` sans aucun write-back et sans classification LLM — les collections et tags existants *sont déjà* la classification humaine.

## Carte des scénarios

**V** = valeur, **E** = effort, notés sur 3.

### Axe « Nettoyer »

| # | Scénario | V | E | Notes |
|---|---|---|---|---|
| N1 | **Doublons d'URL** — normalisation (`utm_*`, `#`, `www.`, slash final) puis regroupement SQL | 3 | 1 | Zéro LLM, zéro dépendance |
| N2 | **Tags redondants** — `dotnet` / `.net` / `dot-net`, tags utilisés une seule fois | 3 | 1 | Distance de Levenshtein sur la taxonomie déjà récupérée à chaque cycle. Rapport seul, jamais d'action automatique |
| N3 | **Liens morts** | 2 | 2 | L'API Raindrop expose déjà un champ `broken` **actuellement ignoré** par `RaindropDto` : le mapper coûte deux lignes et évite d'écrire un crawler |
| N4 | **Péremption** — `ALire` jamais touché après 90 jours → proposition de purge | 2 | 2 | Dépend du signal « traité » (lot 6). Proposition uniquement, jamais de suppression automatique |
| N5 | **Collections déséquilibrées** — collections à 1-2 items, tags orphelins | 1 | 1 | Bonus quasi gratuit du même job que N2 |

### Axe « Synthétiser »

| # | Scénario | V | E | Notes |
|---|---|---|---|---|
| S1 | **Récupération du contenu réel** — fetch HTTP + extraction de texte | 3 | 2 | Débloque S2, S4, S5 et L2. L'excerpt Raindrop (tronqué à 2000 caractères dans le prompt) est trop maigre pour une vraie synthèse |
| S2 | **Résumé par article** — points clés + « pourquoi ça t'intéresse » | 3 | 2 | Un champ `summary` ajouté au tool `classify` existant coûte moins cher qu'un second appel |
| S3 | **Tendances et signaux faibles** — « 7 articles sur MCP ce mois-ci », domaines dominants, évolution des thèmes | 3 | 1 | **Pur SQL, aucun LLM.** Meilleur ratio valeur/effort de la roadmap |
| S4 | **Revue thématique périodique** — « ce mois-ci en .NET », narratif généré par Claude | 3 | 2 | Le livrable phare de l'axe. Nouveau job Coravel mensuel → mail HTML |
| S5 | **Articles liés** — « recoupe X que tu avais sauvé en mars » | 2 | 2 | FTS5 d'abord (gratuit, hors ligne) ; embeddings seulement si insuffisant |
| S6 | **Coût et consommation LLM** | 1 | 1 | Exploitable **rétroactivement** : `ClassificationRawResponse` stocke la réponse Anthropic entière, bloc `usage` compris → `json_extract` suffit |
| S7 | **Export Markdown / Obsidian** — un `.md` par article, frontmatter + résumé | 2 | 1 | Optionnel, alimente un vrai second cerveau |

### Axe « Lire »

| # | Scénario | V | E | Notes |
|---|---|---|---|---|
| L1 | **File de lecture hebdomadaire** — top N scoré, poussé le lundi | 3 | 2 | Complète le digest quotidien exhaustif par un push *sélectif* : la différence entre « voici tout » et « lis ça » |
| L2 | **Temps de lecture estimé** — nombre de mots / 220 | 2 | 1 | Corollaire gratuit de S1. Permet « 25 min de lecture cette semaine » et alimente le scoring de L1 |
| L3 | **Réconciliation** — re-fetch périodique des articles suivis pour détecter l'action humaine (tags modifiés, déplacement, suppression) | 3 | 3 | Le chaînon manquant : sans lui, l'outil ne sait **jamais** si un article a été lu. Débloque N3, N4 et L4 |
| L4 | **Relances** — `ATester` / `Haute` non traité après 14 jours → rappel Discord | 2 | 1 | Trivial une fois L3 en place |
| L5 | **Collection pilote** — « À lire cette semaine » dans Raindrop, alimentée par L1 | 2 | 2 | Fait de Raindrop l'interface de lecture, sans écrire d'application. Implique une écriture **hors « Non trié »** (voir Risques) |

### Axe transverse « Interroger » (MCP)

| # | Scénario | V | E | Notes |
|---|---|---|---|---|
| Q1 | **Serveur MCP en lecture seule** — `search_articles`, `get_article`, `list_recent`, `stats`, `find_similar`, `reading_queue` | 3 | 2 | Sert les trois axes d'un coup. Aucune interface à écrire : Claude Code devient le front |
| Q2 | **Recherche plein texte FTS5** | 3 | 1 | Table virtuelle + triggers de synchronisation. Socle de Q1 et S5 |
| Q3 | **Outils MCP en écriture** — « marque comme lu », « range dans X » | 2 | 2 | Après Q1, et seulement si l'usage le justifie |

## Ordre de bataille

Chaque lot est livrable indépendamment.

### Lot 0 — Fondations techniques

Aucune valeur visible, mais sans lui chaque lot suivant rejoue la même plomberie.

- **Runner de migrations.** Il n'existe aujourd'hui qu'un seul script (`0001_InitialSchema.sql`, ressource embarquée rejouée intégralement au démarrage) et **aucun runner** : la table `SchemaVersion` existe mais personne ne la lit. Lister les ressources `Migrations/NNNN_*.sql`, comparer à `SchemaVersion`, appliquer dans une transaction. Tous les lots suivants ajoutent des colonnes.
- **Élargir `IArticleRepository`.** Le contrat n'expose que cinq méthodes et un seul `SELECT`. Ajouter `GetByIdAsync`, `GetByFilterAsync`, `SearchAsync`, `CountByAsync`, en réutilisant le mapping existant — et cesser au passage de jeter `FetchedAtUtc` et `WriteBackStatus`, aujourd'hui lus depuis SQL puis perdus dans `MapToClassifiedArticle`.
- **Vérifier la disponibilité de FTS5** avant de bâtir dessus :
  ```sql
  SELECT 1 FROM pragma_compile_options WHERE compile_options = 'ENABLE_FTS5';
  ```
  Si absent, replier sur `LIKE` + index, ou changer de paquet natif.

Fichiers concernés : `src/RaindropAI.Infrastructure/Persistence/` (`SqliteConnectionFactory.cs`, `ArticleRepository.cs`, `Migrations/`), `src/RaindropAI.Core/Interfaces/IArticleRepository.cs`.

### Lot 1 — Indexation de la bibliothèque existante

Un `LibraryIndexingJob` distinct, **strictement en lecture seule**, qui parcourt `GET /raindrops/0` (toute la bibliothèque hors corbeille) et remplit `Articles` sans classifier ni écrire chez Raindrop.

- Colonne discriminante (`Source` = `Unsorted` | `Library`) pour ne jamais confondre un article indexé avec un article traité par le pipeline.
- Pagination et curseur propres : **ne pas toucher** au `PollingState` de `UnsortedClassificationJob`.
- Déclenchement rare (cron hebdomadaire, ou flag `Worker__IndexLibraryOnStartup`).
- Mapper au passage les champs du DTO aujourd'hui ignorés qui ont de la valeur : `broken` (N3), `important`, `cover`, et surtout `highlights` — les surlignages sont la matière première idéale pour la synthèse.

⚠️ Volume : potentiellement plusieurs milliers d'items, sur un Raspberry Pi. Pagination, curseur reprenable, rien en mémoire d'un bloc.

### Lot 2 — Hygiène et signaux, sans LLM

Première valeur visible. Un `WeeklyInsightsJob` produit un mail « bilan de la semaine » via un `InsightsMessageBuilder` **pur et statique**, calqué sur `DigestMessageBuilder` (même style : `StringBuilder`, `HtmlEncode`, `InvariantCulture`, et réutilisation directe de `BuildTitleHtml` pour les liens).

Contenu : **N1** doublons · **N2** tags redondants · **N5** collections déséquilibrées · **S3** tendances · **S6** coût LLM.

Zéro appel LLM, zéro nouvelle dépendance, zéro risque sur les données.

### Lot 3 — Serveur MCP en lecture seule

Nouveau projet `src/RaindropAI.Mcp`, SDK C# officiel `ModelContextProtocol` (vérifier la version stable au moment d'attaquer), transport Streamable HTTP, conteneur dédié.

```yaml
# docker-compose.yml
raindropai:            # worker existant, inchangé
raindropai-mcp:        # nouveau
  ports: ["5099:8080"]
  volumes: ["./data:/data:ro"]
```

```jsonc
// .mcp.json, côté poste de développement
{ "raindropai": {
    "type": "http",
    "url": "http://raspberrypi.local:5099/mcp",
    "headers": { "Authorization": "Bearer ..." } } }
```

- Sécurité : LAN uniquement (jamais exposé sur Internet), token bearer obligatoire, connexion SQLite en `Mode=ReadOnly` **et** montage `:ro`.
- Le worker reste le seul writer ([ADR 0002](adr/0002-persistance-sqlite-embarquee.md)) — activer WAL pour que la lecture concurrente ne bloque pas.
- L'image chiselée n'a pas de shell : c'est une image distincte avec son propre `ENTRYPOINT`, pas un `docker exec`.
- Inclut **Q2** : table virtuelle `ArticlesFts` + triggers, via une migration `0002_*.sql`.

⚠️ Ce lot déclenche la clause de réouverture de l'[ADR 0001](adr/0001-architecture-generale.md) : « si le périmètre grossissait significativement (multi-utilisateurs, **API exposée**, etc.), cette architecture devrait être revue ». Un **ADR 0009 est à écrire avant de coder**, justifiant le quatrième projet, le port ouvert, et le maintien de `Core` sans dépendance externe.

### Lot 4 — Contenu réel et résumés

- **S1** : `IContentFetcher` dans Core, implémentation Infrastructure avec `HttpClient` typé + `AddStandardResilienceHandler` (même patron que les trois clients existants) et extraction du texte principal. Colonnes `ContentText`, `ContentFetchedAtUtc`, `ContentStatus`, `WordCount`.
- **Best-effort strict** : paywall, page JS-only, 403, timeout → `ContentStatus` renseigné, `ContentText` nul, et **le pipeline continue**. Même philosophie que `ClassificationResult.Fallback` : un article n'est jamais perdu silencieusement.
- Politesse élémentaire : User-Agent identifiable, timeout court, pas de retry agressif, un seul fetch par article.
- **L2** temps de lecture : corollaire gratuit.
- **S2** résumé : ajouter un champ `summary` au tool `classify` (`ClassificationPromptBuilder`) plutôt qu'un second appel — mais **relever `max_tokens`** (300 aujourd'hui, et `stop_reason == "max_tokens"` est traité comme un échec dur par `AnthropicClassifier`).
- Modèle configurable par usage (`Classifier__SummaryModel`) : le point d'extension est prévu, l'arbitrage Haiku/Sonnet reste ouvert.

### Lot 5 — Synthèse

- **S4** `MonthlyReviewJob` : regroupe le mois par collection ou tag, un appel LLM par thème, mail narratif. Le vrai livrable « qu'est-ce que j'ai appris ce mois-ci ».
- **S5** articles liés via FTS5 (déjà en place depuis le lot 3), exposé aussi comme outil MCP `find_similar`.
- **S7** export Markdown, optionnel.

### Lot 6 — Boucle de retour

- **L3** `ReconciliationJob` : re-fetch des articles suivis, détection des tags et collections modifiés, des articles supprimés, du flag `broken`. Colonnes `LastSeenAtUtc`, `HumanHandledAtUtc`, `LinkStatus`.
- **L4** relances, **N3** liens morts, **N4** péremption : tous triviaux une fois L3 en place.
- **L1** file de lecture, enfin scorée sur des données complètes (priorité × fraîcheur × temps de lecture × non-traité).
- **L5** collection pilote « À lire cette semaine », **seulement après validation explicite** de l'écriture hors « Non trié ».

## Risques et points de vigilance

| Risque | Mitigation |
|---|---|
| **ADR 0001 réouvert** par le lot 3 (API exposée, quatrième projet) | ADR 0009 écrit *avant* le code. `Core` reste à zéro dépendance externe : le projet MCP dépend de `Infrastructure`, jamais l'inverse |
| **Écriture hors « Non trié »** (N1 avec tag, L5) | Invariant historique du projet. Chaque écriture hors périmètre exige une décision explicite et son propre flag, jamais un effet de bord |
| **Concurrence SQLite** (le worker écrit, le MCP lit) | L'ADR 0002 pose « un seul writer à la fois ». Activer WAL, MCP en `Mode=ReadOnly` et montage `:ro` |
| **Volume sur le Pi** (lot 1 : milliers d'items ; lot 4 : contenu HTML) | Pagination, curseur reprenable, `ContentText` tronqué, surveiller la taille du `.db` |
| **Coût LLM multiplié** (lots 4 et 5) | S6 est livré dès le lot 2 : on mesure *avant* de dépenser |
| **Sur-ingénierie** | Le projet a explicitement écarté Clean Architecture, CQRS, MediatR, EF Core et l'abstraction générique de notifieurs ([ADR 0001](adr/0001-architecture-generale.md), [0006](adr/0006-stack-technique.md)). Ces lots ajoutent des jobs et des méthodes de repository — **aucune nouvelle couche** |
| **Le fetch de contenu casse le cycle** | Best-effort strict, jamais bloquant, statut persisté |

## Décisions déjà prises

- Canaux de restitution : **serveur MCP** et **notifications enrichies**. Pas d'interface web.
- Le serveur MCP tourne **sur le Pi**, en HTTP sur le LAN — pas en stdio contre une copie locale de la base.
- La récupération du contenu réel des articles est **retenue** : sans elle, l'axe synthèse reste superficiel.
- Le choix du modèle pour les synthèses (Haiku partout, ou Sonnet pour les tâches à faible volume et forte valeur) est **volontairement laissé ouvert** ; seul le point d'extension est prévu.
