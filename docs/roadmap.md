# Roadmap — exploiter le contenu classé

## Pourquoi ce document

RaindropAI ne fait aujourd'hui que la moitié du travail : il capte les nouveaux articles de « Non trié », les classe, les range, puis notifie (Discord immédiat, digest mail quotidien). Le résultat n'est qu'un **flux sortant** : une fois l'article rangé et l'email envoyé, la base ne sert plus à rien. `ArticleRepository` ne contient d'ailleurs qu'un seul `SELECT` (`GetUnsentDigestItemsAsync`) — tout le reste du schéma est en écriture seule.

Ce document cartographie les scénarios qui transforment cette base en **actif exploitable**, sur quatre axes :

1. **Capter** — élargir la source d'entrée au-delà de « Non trié ».
2. **Synthétiser** — extraire de la valeur agrégée, pas seulement ranger.
3. **Nettoyer** — doublons, liens morts, tags redondants, articles périmés.
4. **Lire** — pousser la bonne chose au bon moment, et boucler la boucle.

Plus un axe transverse **Interroger** (serveur MCP).

C'est une carte, pas un engagement de livraison : les lots sont indépendants et peuvent être pris dans un autre ordre, à condition de respecter les dépendances signalées.

## Décisions actées

Ces points sont tranchés. Ils contraignent tout le reste du document.

| # | Décision | Conséquence |
|---|---|---|
| D1 | **RaindropAI devient un hub multi-sources.** Raindrop n'est plus la source de vérité unique : newsletters Gmail et flux RSS deviennent des sources de premier rang. | Rouvre l'[ADR 0001](adr/0001-architecture-generale.md). Le modèle de données doit être générique **dès le lot 0** (voir ci-dessous). |
| D2 | **Le serveur MCP reste strictement en LAN.** Jamais exposé sur Internet, pas de tunnel, pas d'accès nomade. | Écarte définitivement la migration vers une base hébergée (voir « Neon » dans les arbitrages). SQLite sur le Pi reste le bon choix, l'[ADR 0002](adr/0002-persistance-sqlite-embarquee.md) tient. |
| D3 | **L'email disparaît complètement.** Le digest quotidien n'apporte rien (« liste des articles importés »), et aucun autre canal mail ne le remplace. | Rouvre l'[ADR 0005](adr/0005-canaux-notification.md). Supprime MailKit, `EmailNotifier`, `IDigestNotifier`, `DigestNotificationJob`, `EmailOptions` et la colonne `EmailDigestSentAtUtc`. **Tous les livrables périodiques doivent trouver un autre canal** — voir « Le Markdown devient le format de restitution ». |
| D4 | **Le vault Obsidian est local au PC** (Obsidian Sync / iCloud / Dropbox) et n'est **pas** accessible depuis le Pi. | Aucune écriture Obsidian côté serveur n'est possible. Le pont doit être **tiré depuis le PC**, ce que le MCP en LAN permet nativement. |
| D5 | La récupération du contenu réel des articles est **retenue**. | Sans elle, l'axe synthèse reste superficiel, et les newsletters sont inexploitables. |
| D6 | Le choix du modèle pour les synthèses reste **ouvert** ; seul le point d'extension est prévu (`Classifier__SummaryModel`). | Voir « Coût LLM » dans les arbitrages : ce n'est pas le facteur limitant. |

## Deux prérequis non négociables

### 1. Le corpus est presque vide

Point le plus important de tout ce document.

La table `Articles` ne contient **que ce qui est passé par « Non trié » depuis le premier démarrage**. Toute la bibliothèque déjà triée — des années de veille — est invisible pour l'outil.

Or synthèse, similarité, détection de doublons et statistiques n'ont de valeur **qu'à l'échelle du corpus complet**. Sans indexation préalable, la plupart des scénarios ci-dessous tournent sur quelques dizaines de lignes et ne produisent rien d'intéressant.

L'indexation en lecture seule de toute la bibliothèque (lot 1) est donc le vrai prérequis fonctionnel. Elle ne viole pas l'invariant « on ne touche jamais hors Non trié » : c'est un `GET /raindrops/0` sans aucun write-back et sans classification LLM — les collections et tags existants *sont déjà* la classification humaine.

### 2. Le passage multi-sources doit être absorbé dans le lot 0

Conséquence directe de **D1**, et elle change l'ordre des travaux.

Aujourd'hui `RaindropItem` est le modèle central : le schéma, le repository, le classifieur et les jobs sont tous construits autour de l'identifiant Raindrop. Si le lot 0 pose des fondations Raindrop-spécifiques et que le lot 1 indexe des milliers d'items dessus, la généralisation multi-sources imposera plus tard une **migration de données** sur tout le corpus — exactement le travail qu'on cherche à éviter.

Le modèle générique doit donc exister **avant** la première indexation de masse :

- Un `Item` avec `SourceType` (`Raindrop` | `Newsletter` | `Feed`), `SourceId` (identifiant natif de la source), `Url`, `Title`, `Excerpt`, `CapturedAtUtc`. Clé naturelle : `(SourceType, SourceId)`.
- `RaindropItem` redescend au rang de **DTO d'un adaptateur** parmi d'autres, au lieu d'être le modèle du domaine.
- Une interface `ISourceIngester` dans `Core`, une implémentation par source dans `Infrastructure`, chacune avec **son propre curseur de polling** (la table `PollingState` devient une ligne par source).
- La classification devient source-agnostique : `IClassifier.ClassifyAsync` prend un `Item`, pas un `RaindropItem`.
- Le write-back reste **spécifique à Raindrop** : on ne réécrit ni dans Gmail ni dans un flux RSS. L'invariant « on ne touche jamais hors Non trié » ne disparaît pas, il devient un invariant de *l'adaptateur Raindrop* plutôt qu'un invariant du projet.

C'est la partie la plus structurante de toute la roadmap, et la seule qui coûte plus cher si on la reporte.

## Carte des scénarios

**V** = valeur, **E** = effort, notés sur 3.

### Axe « Capter » (nouveau — issu de D1)

| # | Scénario | V | E | Notes |
|---|---|---|---|---|
| C1 | **Noyau multi-sources** — modèle `Item`, `ISourceIngester`, curseur par source | 3 | 3 | Prérequis de tout l'axe. À faire dans le lot 0 (voir ci-dessus), pas après |
| C2 | **Newsletters Gmail** — ingestion des mails portant un label Gmail donné | 3 | 3 | API Gmail en scope `gmail.readonly`, `users.messages.list?q=label:<tag>`. Le corps HTML doit être extrait : **même besoin que S1**, donc à faire après le lot 4, pas avant |
| C3 | **Flux RSS** — remplacement de Feedly | 3 | 2 | Voir l'arbitrage Miniflux ci-dessous : l'ingestion RSS directe dans RaindropAI est plus simple que d'auto-héberger Miniflux *pour ce besoin-là* |
| C4 | **Veille automatique sur sujets** — surveiller des thèmes définis, pas seulement ce qu'on sauve | 3 | 2 | Se construit sur C3 (flux de recherche RSS) + filtrage LLM contre le corpus existant. Voir l'arbitrage « recherche web » : la recherche web facturée à l'appel est le seul poste qui menace vraiment le budget |
| C5 | **Déduplication inter-sources** — le même article capté par Raindrop, une newsletter et un flux | 2 | 1 | Corollaire de N1 une fois C1 en place : la normalisation d'URL sert de clé de rapprochement |

### Axe « Nettoyer »

| # | Scénario | V | E | Notes |
|---|---|---|---|---|
| N1 | **Doublons d'URL** — normalisation (`utm_*`, `#`, `www.`, slash final) puis regroupement SQL | 3 | 1 | Zéro LLM, zéro dépendance. Devient inter-sources avec C5 |
| N2 | **Tags redondants** — `dotnet` / `.net` / `dot-net`, tags utilisés une seule fois | 3 | 1 | Distance de Levenshtein sur la taxonomie déjà récupérée à chaque cycle. Rapport seul, jamais d'action automatique |
| N3 | **Liens morts** | 2 | 2 | L'API Raindrop expose déjà un champ `broken` **actuellement ignoré** par `RaindropDto` : le mapper coûte deux lignes et évite d'écrire un crawler |
| N4 | **Péremption** — `ALire` jamais touché après 90 jours → proposition de purge | 2 | 2 | Dépend du signal « traité » (lot 6). Proposition uniquement, jamais de suppression automatique |
| N5 | **Collections déséquilibrées** — collections à 1-2 items, tags orphelins | 1 | 1 | Bonus quasi gratuit du même job que N2 |

### Axe « Synthétiser »

| # | Scénario | V | E | Notes |
|---|---|---|---|---|
| S1 | **Récupération du contenu réel** — fetch HTTP + extraction de texte | 3 | 2 | Débloque S2, S4, S5, L2 **et C2**. L'excerpt Raindrop (tronqué à 2000 caractères dans le prompt) est trop maigre pour une vraie synthèse |
| S2 | **Résumé par article** — points clés + « pourquoi ça t'intéresse » | 3 | 2 | Un champ `summary` ajouté au tool `classify` existant coûte moins cher qu'un second appel |
| S3 | **Tendances et signaux faibles** — « 7 articles sur MCP ce mois-ci », domaines dominants, évolution des thèmes | 3 | 1 | **Pur SQL, aucun LLM.** Meilleur ratio valeur/effort de la roadmap |
| S4 | **Revue thématique périodique** — « ce mois-ci en .NET », narratif généré par Claude | 3 | 2 | Le livrable phare de l'axe. Nouveau job Coravel mensuel → **fichier Markdown** (plus de mail, cf. D3) |
| S5 | **Articles liés** — « recoupe X que tu avais sauvé en mars » | 2 | 2 | FTS5 d'abord (gratuit, hors ligne) ; embeddings seulement si insuffisant |
| S6 | **Coût et consommation LLM** | 2 | 1 | Exploitable **rétroactivement** : `ClassificationRawResponse` stocke la réponse Anthropic entière, bloc `usage` compris → `json_extract` suffit. Valeur relevée à 2 : c'est le garde-fou du budget de 10 €/mois |
| S7 | **Base d'outils** — table dédiée + projection Markdown vers Obsidian | 3 | 2 | Voir « Le pont Obsidian » ci-dessous. La source de vérité reste RaindropAI ; Obsidian n'est qu'une projection |
| S8 | **Export Markdown du corpus** — un `.md` par article, frontmatter + résumé | 2 | 1 | Même mécanique que S7. Alimente un vrai second cerveau |

### Axe « Lire »

| # | Scénario | V | E | Notes |
|---|---|---|---|---|
| L1 | **File de lecture hebdomadaire** — top N scoré | 3 | 2 | Remplace le digest quotidien exhaustif supprimé (D3) : la différence entre « voici tout » et « lis ça ». Canal : Discord + MCP |
| L2 | **Temps de lecture estimé** — nombre de mots / 220 | 2 | 1 | Corollaire gratuit de S1. Permet « 25 min de lecture cette semaine » et alimente le scoring de L1 |
| L3 | **Réconciliation** — re-fetch périodique des articles suivis pour détecter l'action humaine (tags modifiés, déplacement, suppression) | 3 | 3 | Le chaînon manquant : sans lui, l'outil ne sait **jamais** si un article a été lu. Débloque N3, N4 et L4. Ne concerne que la source Raindrop |
| L4 | **Relances** — `ATester` / `Haute` non traité après 14 jours → rappel Discord | 2 | 1 | Trivial une fois L3 en place |
| L5 | **Collection pilote** — « À lire cette semaine » dans Raindrop, alimentée par L1 | 2 | 2 | Fait de Raindrop l'interface de lecture, sans écrire d'application. Implique une écriture **hors « Non trié »** (voir Risques) |
| L6 | **Recoupement avec les projets Obsidian** — « quels articles parlent de ce sur quoi je bosse ? » | 3 | 1 | Effort 1 parce qu'il n'y a **rien à écrire** : Claude Code lit le vault en local et interroge le MCP sur le LAN. Le recoupement se fait dans le client. Voir « Le pont Obsidian » |

### Axe transverse « Interroger » (MCP)

| # | Scénario | V | E | Notes |
|---|---|---|---|---|
| Q1 | **Serveur MCP en lecture seule** — `search_items`, `get_item`, `list_recent`, `stats`, `find_similar`, `reading_queue`, `list_tools` | 3 | 2 | Sert les quatre axes d'un coup. Aucune interface à écrire : Claude Code devient le front. **Devient le canal principal** maintenant que le mail disparaît (D3) |
| Q2 | **Recherche plein texte FTS5** | 3 | 1 | Table virtuelle + triggers de synchronisation. Socle de Q1 et S5 |
| Q3 | **Outils MCP en écriture** — « marque comme lu », « range dans X » | 2 | 2 | Après Q1, et seulement si l'usage le justifie |

## Le Markdown devient le format de restitution

Conséquence de **D3** qu'il faut regarder en face : supprimer l'email ne supprime pas le besoin de restituer. Le bilan hebdomadaire (lot 2) et la revue mensuelle (lot 5) doivent atterrir quelque part.

Trois canaux restent, et ils se répartissent proprement :

| Type de contenu | Canal | Pourquoi |
|---|---|---|
| Alerte unitaire, immédiate | **Discord** (existant, inchangé) | Un article, une ligne, temps réel |
| Rapport périodique (bilan hebdo, revue mensuelle) | **Fichier Markdown poussé sur Discord en pièce jointe** | Un webhook Discord accepte les fichiers. Le contenu narratif dépasse largement les 2000 caractères d'un message et les 4096 d'un embed — la pièce jointe évite le découpage et reste lisible |
| Interrogation à la demande | **MCP** | C'est exactement pour ça qu'il existe |

Le choix du Markdown n'est pas cosmétique : c'est le **même format** que les fiches outils (S7), l'export corpus (S8) et le vault Obsidian. Un seul générateur, trois usages. Concrètement, `DigestMessageBuilder` (aujourd'hui producteur de HTML pour mail) est remplacé par un `MarkdownReportBuilder` pur et statique, calqué sur le même style (`StringBuilder`, `InvariantCulture`, sans état).

Bénéfice collatéral : disparition de MailKit, de la configuration SMTP, et d'`EmailNotifier` — aujourd'hui à **0 % de couverture de tests**, seule classe du projet dans ce cas.

## Le pont Obsidian

**D4** pose la contrainte : le Pi ne voit pas le vault. Toute écriture doit donc partir du PC. Le MCP en LAN strict (**D2**) est précisément le bon outil pour ça — le PC est sur le LAN.

**Le sens de la synchro est descendant, comme demandé** : la source de vérité centralisée et gratuite, c'est RaindropAI lui-même (SQLite sur le Pi). Pas besoin d'introduire Notion, Airtable ou un service tiers : la base existe déjà, elle est gratuite, elle est chez toi, et le MCP l'expose.

Deux variantes, à prendre dans cet ordre :

1. **À la demande, sans une ligne de code.** Claude Code a le vault en local et le MCP sur le LAN. « Génère la fiche de l'outil X dans mon vault », « quels articles recoupent mes notes du projet Y » — le recoupement se fait dans le client, avec le vault d'un côté et le corpus de l'autre. C'est **L6, et c'est gratuit** : le seul prérequis est le lot 3.
2. **Automatisé, si l'usage se confirme.** Un petit CLI d'export (`raindropai-export --vault <chemin>`) appelé par une tâche planifiée Windows, qui interroge le MCP et écrit les `.md`. À n'écrire que si la variante 1 devient fastidieuse.

Pour la **base d'outils** (S7) : table `Tools` en base (nom, catégorie, statut d'évaluation, articles liés, verdict), alimentée par la classification quand `Action == ATester`, projetée en une fiche `.md` par outil. La fiche est **régénérée**, jamais éditée à la main dans Obsidian — sinon on crée un conflit de source de vérité. Si tu veux annoter, l'annotation doit vivre dans un fichier voisin (`X.notes.md`) que la projection ne touche pas.

## Ordre de bataille

Chaque lot est livrable indépendamment. Deux phases : exploiter ce qu'on a, puis élargir les sources.

### Phase 1 — Exploiter l'existant

#### Lot 0 — Fondations techniques *(élargi par D1)*

Aucune valeur visible, mais sans lui chaque lot suivant rejoue la même plomberie — et sans le volet multi-sources, chaque lot suivant devra être migré.

- **Runner de migrations.** Il n'existe aujourd'hui qu'un seul script (`0001_InitialSchema.sql`, ressource embarquée rejouée intégralement au démarrage) et **aucun runner** : la table `SchemaVersion` existe mais personne ne la lit. Lister les ressources `Migrations/NNNN_*.sql`, comparer à `SchemaVersion`, appliquer dans une transaction.
- **Modèle `Item` générique + `ISourceIngester`** (voir « prérequis n°2 »). `PollingState` devient une ligne par source. Le `RaindropClient` actuel devient la première implémentation d'`ISourceIngester`, sans changement de comportement.
- **Élargir `IArticleRepository`.** Le contrat n'expose que cinq méthodes et un seul `SELECT`. Ajouter `GetByIdAsync`, `GetByFilterAsync`, `SearchAsync`, `CountByAsync` — et cesser au passage de jeter `FetchedAtUtc` et `WriteBackStatus`, aujourd'hui lus depuis SQL puis perdus dans `MapToClassifiedArticle`.
- **Vérifier la disponibilité de FTS5** avant de bâtir dessus :
  ```sql
  SELECT 1 FROM pragma_compile_options WHERE compile_options = 'ENABLE_FTS5';
  ```
  Si absent, replier sur `LIKE` + index, ou changer de paquet natif.

Fichiers concernés : `src/RaindropAI.Core/Models/`, `src/RaindropAI.Core/Interfaces/`, `src/RaindropAI.Infrastructure/Persistence/` (`SqliteConnectionFactory.cs`, `ArticleRepository.cs`, `Migrations/`), `src/RaindropAI.Infrastructure/Raindrop/`.

⚠️ **ADR 0009 à écrire avant de coder** : le passage multi-sources rouvre l'ADR 0001 bien plus franchement que le serveur MCP ne le faisait.

#### Lot 1 — Indexation de la bibliothèque existante

Un `LibraryIndexingJob` distinct, **strictement en lecture seule**, qui parcourt `GET /raindrops/0` (toute la bibliothèque hors corbeille) et remplit `Items` sans classifier ni écrire chez Raindrop.

- Discriminant : `SourceType = Raindrop` + un marqueur d'origine (`Unsorted` | `Library`) pour ne jamais confondre un article indexé avec un article traité par le pipeline.
- Pagination et curseur propres : **ne pas toucher** au curseur de `UnsortedClassificationJob`.
- Déclenchement rare (cron hebdomadaire, ou flag `Worker__IndexLibraryOnStartup`).
- Mapper au passage les champs du DTO aujourd'hui ignorés qui ont de la valeur : `broken` (N3), `important`, `cover`, et surtout `highlights` — les surlignages sont la matière première idéale pour la synthèse, et ils sont perdus à chaque cycle.

⚠️ Volume : potentiellement plusieurs milliers d'items, sur un Raspberry Pi. Pagination, curseur reprenable, rien en mémoire d'un bloc.

#### Lot 2 — Hygiène, signaux, et retrait de l'email

Première valeur visible, et exécution de **D3**.

- Retirer `DigestNotificationJob`, `IDigestNotifier`, `EmailNotifier`, `EmailOptions`, `DigestMessageBuilder`, la dépendance MailKit et la colonne `EmailDigestSentAtUtc` (migration `0002_*.sql`).
- Introduire `MarkdownReportBuilder` (pur, statique) + l'envoi de pièce jointe via le webhook Discord existant.
- Un `WeeklyInsightsJob` produit le rapport : **N1** doublons · **N2** tags redondants · **N5** collections déséquilibrées · **S3** tendances · **S6** coût LLM.

Zéro appel LLM, zéro nouvelle dépendance, et une dépendance en moins. Le seul risque est de perdre le filet « rien ne se perd » qu'assurait le digest exhaustif — c'est un choix assumé (D3), compensé plus tard par L1.

#### Lot 3 — Serveur MCP en lecture seule (LAN strict)

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

- Sécurité : **LAN uniquement (D2)**, aucune redirection de port sur la box, token bearer obligatoire malgré tout, connexion SQLite en `Mode=ReadOnly` **et** montage `:ro`.
- Le worker reste le seul writer ([ADR 0002](adr/0002-persistance-sqlite-embarquee.md)) — activer WAL pour que la lecture concurrente ne bloque pas.
- L'image chiselée n'a pas de shell : c'est une image distincte avec son propre `ENTRYPOINT`, pas un `docker exec`.
- Inclut **Q2** : table virtuelle `ItemsFts` + triggers.
- Débloque **L6** immédiatement, sans code supplémentaire.

⚠️ ADR 0010 à écrire : quatrième projet, port ouvert sur le LAN, maintien de `Core` sans dépendance externe.

#### Lot 4 — Contenu réel et résumés

- **S1** : `IContentFetcher` dans Core, implémentation Infrastructure avec `HttpClient` typé + `AddStandardResilienceHandler` (même patron que les trois clients existants) et extraction du texte principal. Colonnes `ContentText`, `ContentFetchedAtUtc`, `ContentStatus`, `WordCount`.
- **Best-effort strict** : paywall, page JS-only, 403, timeout → `ContentStatus` renseigné, `ContentText` nul, et **le pipeline continue**. Même philosophie que `ClassificationResult.Fallback` : un article n'est jamais perdu silencieusement.
- Politesse élémentaire : User-Agent identifiable, timeout court, pas de retry agressif, un seul fetch par article.
- **L2** temps de lecture : corollaire gratuit.
- **S2** résumé : ajouter un champ `summary` au tool `classify` (`ClassificationPromptBuilder`) plutôt qu'un second appel — mais **relever `max_tokens`** (300 aujourd'hui, et `stop_reason == "max_tokens"` est traité comme un échec dur par `AnthropicClassifier`).
- Modèle configurable par usage (`Classifier__SummaryModel`) : le point d'extension est prévu, l'arbitrage reste ouvert (D6).

⚠️ C'est le lot qui coûte. Ne l'attaquer qu'avec S6 en place (lot 2) et en commençant par un sous-ensemble, pas par le corpus complet.

#### Lot 5 — Synthèse et projections

- **S4** `MonthlyReviewJob` : regroupe le mois par collection ou tag, un appel LLM par thème, rapport narratif en Markdown poussé sur Discord.
- **S5** articles liés via FTS5 (déjà en place depuis le lot 3), exposé aussi comme outil MCP `find_similar`.
- **S7** base d'outils + projection Markdown, **S8** export corpus. Même `MarkdownReportBuilder` que le lot 2.

#### Lot 6 — Boucle de retour

- **L3** `ReconciliationJob` : re-fetch des items Raindrop suivis, détection des tags et collections modifiés, des articles supprimés, du flag `broken`. Colonnes `LastSeenAtUtc`, `HumanHandledAtUtc`, `LinkStatus`.
- **L4** relances, **N3** liens morts, **N4** péremption : tous triviaux une fois L3 en place.
- **L1** file de lecture, enfin scorée sur des données complètes (priorité × fraîcheur × temps de lecture × non-traité). C'est ici que le filet perdu au lot 2 est remplacé par quelque chose de mieux.
- **L5** collection pilote « À lire cette semaine », **seulement après validation explicite** de l'écriture hors « Non trié ».

### Phase 2 — Élargir les sources

Ne commence qu'une fois le lot 4 livré : les deux connecteurs ont besoin de l'extraction de contenu.

#### Lot 7 — Connecteur RSS (**C3**)

`FeedIngester` implémentant `ISourceIngester` : liste de flux en configuration ou en base, parsing via `System.ServiceModel.Syndication`, curseur par flux sur la date de publication. Les entrées deviennent des `Item` avec `SourceType = Feed` et passent dans le pipeline de classification existant.

Remplace Feedly sans rien auto-héberger de plus (voir l'arbitrage Miniflux).

#### Lot 8 — Connecteur newsletters Gmail (**C2**)

`GmailIngester` : OAuth Google en scope `gmail.readonly`, `users.messages.list` filtré sur `q=label:<tag>`, extraction du corps HTML avec le même extracteur que S1, curseur sur `historyId`.

⚠️ Deux points de vigilance : le refresh token doit être stocké (jamais en clair dans le dépôt, cf. `.env`), et une newsletter contient typiquement **plusieurs liens** — il faut décider si l'unité est le mail ou chaque lien qu'il contient. Recommandation : le mail comme `Item`, les liens extraits comme items secondaires rattachés, sinon le corpus explose en bruit.

#### Lot 9 — Veille automatique (**C4**)

Sujets définis en configuration → flux de recherche (RSS) → filtrage LLM contre le corpus existant pour ne remonter que ce qui est nouveau et pertinent → alerte Discord.

Construit sur le lot 7. **Ne pas partir sur une recherche web facturée à l'appel** avant d'avoir mesuré : voir l'arbitrage ci-dessous.

#### Lot 10 — Déduplication inter-sources (**C5**)

Une fois trois sources actives, le même article arrive plusieurs fois. Réutilise la normalisation d'URL de N1 comme clé de rapprochement, avec un `Item` canonique et des occurrences rattachées.

## Arbitrages tranchés

### Neon Postgres gratuit — **écarté**

Trois raisons, dans l'ordre :

1. **D2 supprime le besoin.** La seule bonne raison de quitter SQLite était de rendre la base accessible depuis l'extérieur. Décision prise : le MCP reste en LAN. Il ne reste aucun bénéfice.
2. **Le plan gratuit est trop juste.** [0,5 Go de stockage et 100 CU-heures/mois par projet](https://neon.com/faqs/free-plan-limits-and-quotas). Le lot 1 (des milliers d'items) tient, le lot 4 (`ContentText` en clair pour chaque article) probablement pas. Découvrir la limite après avoir migré serait le pire scénario.
3. **Ça fragilise le worker.** Aujourd'hui une coupure Internet interrompt les appels Raindrop et Anthropic mais la base reste saine et locale. Avec une base distante, le worker ne peut plus rien persister — y compris son propre curseur de polling.

L'[ADR 0002](adr/0002-persistance-sqlite-embarquee.md) reste valide. À rouvrir seulement si D2 change.

### Coût LLM et autre fournisseur — **pas le sujet, mais à instrumenter**

Ordre de grandeur, avec Claude Haiku 4.5 à 1 $ / 5 $ par million de tokens :

| Opération | Tokens entrée / sortie | Coût unitaire |
|---|---|---|
| Classification d'un article (aujourd'hui) | ~3 000 / ~300 | **~0,004 $** |
| Classification + résumé sur contenu réel (lot 4) | ~8 000 / ~800 | **~0,012 $** |
| Revue mensuelle par thème (lot 5) | ~30 000 / ~2 000 | **~0,04 $** |

Avec un budget de 10 €/mois : environ **2 500 articles/mois** au régime actuel, ou **900 articles/mois** avec contenu réel et résumés. Le volume de « Non trié » est très en dessous.

Ce qui peut réellement faire mal, c'est le **backfill** : classifier d'un coup une bibliothèque de plusieurs milliers d'articles avec résumés, c'est 30 à 60 € en une nuit. D'où l'ordre imposé — **S6 livré au lot 2, bien avant le lot 4** — et le principe : le lot 1 indexe *sans* classifier, l'enrichissement se fait par lots contrôlés.

Conclusion : changer de fournisseur ferait économiser quelques euros au prix d'une réimplémentation d'`IClassifier` et de la perte du tool-use forcé sur lequel repose toute la fiabilité de la classification. **Non rentable.** Le point d'extension existe (D6) si la donne change ; on ne l'utilise pas par anticipation.

### Recherche web pour la veille (C4) — **le vrai risque budgétaire**

C'est le seul poste facturé *à l'appel* et non au token. Une veille quotidienne sur une dizaine de sujets peut à elle seule consommer le budget mensuel.

Ordre recommandé : **flux RSS de recherche d'abord** (gratuit, illimité, déjà couvert par le lot 7), LLM uniquement pour filtrer et dédupliquer contre le corpus. La recherche web facturée n'est justifiée que si le RSS s'avère insuffisant, et alors avec un plafond dur d'appels par cycle.

### Abonnement Claude vs crédits API — **à vérifier avant d'en dépendre**

Ton abonnement Claude ne donne **pas** d'accès API programmatique : ce sont deux facturations distinctes, et il n'existe pas de moyen supporté de faire consommer un abonnement par `AnthropicClassifier`.

Une piste existe : faire tourner le traitement via Claude Code / l'Agent SDK en mode headless, authentifié avec le profil OAuth de l'abonnement plutôt qu'avec une clé API. C'est techniquement faisable, mais ça change la nature du projet (on ne pilote plus un appel HTTP, on pilote un agent) et **les conditions d'usage sont à vérifier avant d'en faire une dépendance de production**.

Vu les chiffres ci-dessus, l'enjeu est de quelques euros par mois. À garder en question ouverte, pas à traiter en priorité.

### Miniflux auto-hébergé — **utile, mais pas pour l'ingestion**

Il y a deux besoins distincts derrière « migrer Feedly vers Miniflux » :

| Besoin | Réponse |
|---|---|
| **Une interface de lecture** pour remplacer Feedly côté humain | Miniflux fait très bien le travail : léger, écrit en Go, tourne sans problème sur un Pi. À noter : il **exige Postgres**, donc un conteneur de plus sur la machine |
| **Ingérer des flux dans le pipeline** | Ingestion RSS directe dans RaindropAI (lot 7). Passer par Miniflux ajouterait un intermédiaire, une base Postgres et une API à interroger pour du contenu qu'on sait parser en une classe |

Les deux ne s'excluent pas, et le bon ordre est : **lot 7 d'abord** (le pipeline fonctionne), Miniflux ensuite *si* tu veux l'interface de lecture. Dans ce cas, on lui prend ses flux via son API REST plutôt que de maintenir deux listes d'abonnements.

### Un outil existe-t-il déjà ? — **spike à faire avant le lot 3**

[OpenClaw](https://github.com/openclaw/openclaw) recouvre réellement une partie du périmètre : runtime d'agent auto-hébergé, connecteurs Discord, mémoire longue, skills auto-écrites, et surtout support multi-fournisseurs (Anthropic, OpenAI, Gemini, OpenRouter…). Il ne classe pas des raindrops et ne connaît pas ta taxonomie, mais il pourrait absorber l'axe « veille automatique » (lot 9) et une partie du rôle du MCP.

Ce qu'il ne remplace pas : le pipeline de classification, le write-back Raindrop avec ses invariants, et le corpus persistant qui fait toute la valeur des axes Synthétiser et Nettoyer.

Recommandation : **un spike d'une demi-journée avant d'attaquer le lot 3**, pas après. Le lot 3 est le premier qui construit une surface qu'OpenClaw pourrait fournir. Les lots 0 à 2 sont utiles quoi qu'il arrive.

## Risques et points de vigilance

| Risque | Mitigation |
|---|---|
| **ADR 0001 rouvert** par D1 (multi-sources) et par le lot 3 (quatrième projet) | ADR 0009 (multi-sources) et 0010 (MCP) écrits *avant* le code. `Core` reste à zéro dépendance externe : le projet MCP dépend de `Infrastructure`, jamais l'inverse |
| **Migration de données oubliée** si le multi-sources arrive après le lot 1 | C'est exactement pourquoi C1 est dans le lot 0. Ne pas indexer des milliers d'items sur un schéma qu'on sait devoir changer |
| **Perte du filet « rien ne se perd »** par la suppression du digest (D3) | Assumé au lot 2, compensé au lot 6 par L1. Entre les deux, seul Discord alerte — accepter cette fenêtre ou reporter la suppression après L1 |
| **Écriture hors « Non trié »** (N1 avec tag, L5) | Invariant historique du projet. Chaque écriture hors périmètre exige une décision explicite et son propre flag, jamais un effet de bord |
| **Concurrence SQLite** (le worker écrit, le MCP lit) | L'ADR 0002 pose « un seul writer à la fois ». Activer WAL, MCP en `Mode=ReadOnly` et montage `:ro` |
| **Volume sur le Pi** (lot 1 : milliers d'items ; lot 4 : contenu HTML ; phase 2 : trois sources) | Pagination, curseur reprenable, `ContentText` tronqué, surveiller la taille du `.db` |
| **Coût LLM multiplié** (lots 4, 5 et 9) | S6 est livré dès le lot 2 : on mesure *avant* de dépenser. Backfill par lots, jamais d'un bloc. Plafond dur sur la recherche web |
| **Explosion du bruit** en phase 2 (une newsletter = 20 liens) | Le mail comme unité d'`Item`, pas chaque lien. C5/lot 10 pour la déduplication |
| **Secrets Gmail** (refresh token OAuth) | `dotnet user-secrets` en local, variables d'environnement en production, jamais dans le dépôt. Scope strictement `gmail.readonly` |
| **Conflit de source de vérité avec Obsidian** | Les fiches générées sont **régénérées, jamais éditées**. Les annotations humaines vivent dans des fichiers voisins que la projection ne touche pas |
| **Sur-ingénierie** | Le projet a explicitement écarté Clean Architecture, CQRS, MediatR, EF Core et l'abstraction générique de notifieurs ([ADR 0001](adr/0001-architecture-generale.md), [0006](adr/0006-stack-technique.md)). Le multi-sources ajoute **une** interface (`ISourceIngester`) et un modèle générique — pas une couche |

## Questions ouvertes

- **« Shadow »** et **« Hermes »** : références non identifiées dans le commentaire de la PR #32. À clarifier avant de pouvoir les évaluer.
- **Abonnement Claude en mode headless** : faisabilité technique établie, conditions d'usage à vérifier. Enjeu financier faible (quelques euros/mois).
- **Unité d'ingestion d'une newsletter** : le mail, ou chaque lien qu'il contient ? Recommandation ci-dessus (le mail), à confirmer à l'usage.
- **Embeddings pour S5** : seulement si FTS5 s'avère insuffisant. Ajouterait une dépendance et un coût récurrent.
