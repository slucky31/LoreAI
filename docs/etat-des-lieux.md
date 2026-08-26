# État des lieux

> Fichier court, **réécrit et jamais complété**. Il répond à une seule question : *où on en est, là, maintenant.*
> L'historique est dans `git log` et les Releases. La cible est dans [`roadmap.md`](roadmap.md). Les décisions sont dans [`adr/`](adr/).
> Le suivi des lots est dans les [issues #41 à #51](https://github.com/slucky31/LoreAI/issues?q=is%3Aissue+milestone%3A*).

**Dernière mise à jour :** 2026-08-26 · **Version publiée :** 0.15.0

---

## Où on en est

| | |
|---|---|
| **Lot en cours** | **Lot 8 ([#49](https://github.com/slucky31/LoreAI/issues/49)), connecteur newsletters Gmail, en cours sur deux PR.** PR A (`refactor/lot8-article-source-key`, [#81](https://github.com/slucky31/LoreAI/pull/81)) : `Articles.Id` devient généré par la base, clé applicative `(SourceType, SourceId)` — prérequis identifié par l'ADR 0012, ouverte, 319/319 tests. PR B (`feat/lot8-gmail-connector`, pas encore poussée/PR) : `GmailIngester` (`HttpClient` brut, refresh OAuth par appel, curseur `historyId` géré par l'ingesteur lui-même), filtre heuristique + `IEmailLinkExtractor`/`AnthropicEmailLinkExtractor` (tool-use, symétrique à `IClassifier`), `EmailIngestionJob` (jamais de write-back Raindrop, un repli/échec sur un lien n'interrompt pas le lot), comptabilisation S6 (`EmailExtractionLogs`) combinée au rapport hebdomadaire. **Désactivé par défaut** (`Worker__EmailIngestionEnabled=false`) : pas de projet Google Cloud/client OAuth créé encore, donc zéro test réel possible, tout est validé via WireMock/NSubstitute (363/363 tests). **L5 (collection pilote) toujours exclu**, sans rapport et conditionné à une validation explicite de l'écriture hors « Non trié ». |
| **Prochain geste** | Ouvrir la PR lot 8 partie B (dépend de la PR A #81, à merger d'abord). Une fois les deux mergées : créer le projet Google Cloud + client OAuth (procédure dans le README, section « Connecteur newsletters Gmail »), obtenir un refresh token, seeder `PollingStates` (`SourceType='Newsletter'`) avec le `historyId` courant, puis `Worker__EmailIngestionEnabled=true` en prod pour un premier passage réel. Indépendamment : `MonthlyReviewJob` (lot 5) et `WeeklyInsightsJob` (lot 2) restent à re-vérifier au prochain passage naturel. |
| **Dernière décision** | **#34 (cache de prompt) tranché par une mesure réelle, 2026-08-24 : sans effet, comme prévu.** 5 échantillons post-déploiement du lot 4 (`cache_control` posé sur le tool `classify`) : `cache_creation_input_tokens`/`cache_read_input_tokens` à 0 partout — le préfixe cacheable (system + tools, ~900 tokens estimés) reste bien sous le seuil de 4096 tokens de Claude Haiku 4.5. Sujet clos jusqu'à un vrai backfill (seul scénario où le cache change la facture, cf. roadmap) ; le marqueur reste en place, sans effet ni coût tant que le seuil n'est pas atteint. |
| **Bloqué par** | Rien. Le Pi (`mcm8`) est en ligne. |

## Ce qui tourne aujourd'hui

**Sur `main`, pas encore sur `mcm8` (voir tableau ci-dessus) :** le worker classe la collection « Non trié » toutes les 15 min (Claude Haiku, tool-use forcé), applique tags et déplacements dans Raindrop, alerte sur Discord les articles `ATester` / `Haute`, et envoie un compte-rendu Discord à la fin de chaque cycle ayant traité au moins un article. Plus d'email : le canal a été retiré (D3, ADR 0013). Persistance PostgreSQL (instance mutualisée sur `mcm8`, EF Core), modèle `Item` générique multi-sources, journal `CycleRuns` et healthcheck Docker (lot 0). `LibraryIndexingJob` (hebdomadaire) indexe en lecture seule toute la bibliothèque Raindrop déjà triée par l'utilisateur (lot 1) ; `WeeklyInsightsJob` (hebdomadaire, juste après) lit cet index pour envoyer un rapport Markdown en pièce jointe Discord (doublons, tags à nettoyer, collections déséquilibrées, tendances, coût LLM) — zéro appel LLM, zéro écriture (lot 2). Déploiement par image `ghcr.io` multi-arch, le Pi ne fait que `pull`.

**Réellement en production sur `mcm8` (v0.8.0) :** tout ce qui précède moins le compte-rendu de cycle, moins `WeeklyInsightsJob`, et avec le digest email encore actif à la place du retrait D3.

Le reste de la roadmap (hygiène/signaux, MCP, contenu réel, sources Gmail/RSS...) n'est pas encore implémenté.

## Décisions actées, non encore appliquées

| | Décision | Où c'est écrit |
|---|---|---|
| D1 | Hub multi-sources (Raindrop + Gmail + RSS) | [roadmap](roadmap.md), [ADR 0012](adr/0012-modele-item-generique-multi-sources.md) ✅ (modèle `Item`/`ISourceIngester` — connecteurs Gmail/RSS encore à écrire, lots 7-8) |
| D2 | Réseau privé strict — LAN **ou tailnet**, jamais d'exposition publique | [ADR 0010](adr/0010-topologie-reseau-tailscale.md) ✅ |
| D4 | Le vault Obsidian n'est pas visible du Pi | [roadmap](roadmap.md) |
| D5 | On récupère le contenu réel des articles | [roadmap](roadmap.md) |
| D6 | Modèle de synthèse non tranché | [roadmap](roadmap.md) |
| D7 | PostgreSQL mutualisé, EF Core (pas Dapper) | [ADR 0009](adr/0009-postgresql-mutualise-sur-le-pi.md) ✅, [ADR 0011](adr/0011-ef-core-remplace-dapper.md) ✅ |

## À trancher par une mesure, pas par un débat

- ~~**Spike OpenClaw**~~ — **tranché, 2026-08-23.** OpenClaw est un *client* MCP (se connecte à des serveurs existants), pas un serveur — il ne peut pas remplacer le MCP du lot 3, qui doit être écrit quoi qu'il arrive. Détail dans [roadmap.md](roadmap.md#un-outil-existe-t-il-déjà--spike-fait-2026-08-23--le-lot-3-reste-nécessaire).
- **Cache de prompt** ([#34](https://github.com/slucky31/LoreAI/issues/34)) — 30 min de mesure, à faire au lot 4. A priori sans effet au volume actuel, décisif au backfill.
- ~~**Docker sur le poste de travail**~~ — **tranché.** Le Shadow PC ne peut pas faire tourner Docker (`HCS_E_HYPERV_NOT_INSTALLED` : pas de virtualisation imbriquée). Le développement se fait donc depuis un environnement qui en dispose, et les tests utilisent `Testcontainers.PostgreSql`.

## Environnement, à ne pas re-découvrir

- **Deux postes.** `shadow-p9t9qc7h` (**Shadow PC**, Windows hébergé dans le cloud) et **`afl-it-ndu`**, la machine de développement Docker-capable — dessus, le travail se fait dans la distro WSL2 **`Ubuntu-perso`**. Aucun des deux postes n'est sur le LAN domestique — c'est ce constat qui a invalidé la formulation d'origine de D2, voir [ADR 0010](adr/0010-topologie-reseau-tailscale.md).
- **Le développement et les tests se font depuis `Ubuntu-perso` (sur `afl-it-ndu`)**, puisque `Testcontainers.PostgreSql` exige un daemon Docker. Le Shadow reste utilisable pour le reste : lecture, rédaction, `git`, appels d'API.
- **Le Shadow ne pourra jamais exécuter la suite de tests** : sa plateforme n'expose pas la virtualisation imbriquée (`HCS_E_HYPERV_NOT_INSTALLED`), donc ni WSL2, ni Hyper-V, ni Docker Desktop. Inutile de réessayer.
- **`Ubuntu-perso` a eu besoin d'une mise en route, désormais faite mais pas automatisée — utile si un jour reprovisionnée :**
  1. Docker Desktop (Windows) tournait déjà sur `afl-it-ndu` mais son intégration WSL doit être activée **par distro** — `Settings → Resources → WSL Integration`, cocher `Ubuntu-perso`, puis rouvrir un terminal (le shell déjà ouvert ne voit pas le changement). *(Une alternative sans Docker Desktop — Docker CE directement dans la distro, ou Rancher Desktop/Podman — évite la question de licence, mais n'a pas été le chemin pris ici.)*
  2. Le SDK .NET n'y était **pas préinstallé**. Installé via `dotnet-install.sh --version <celle de global.json>` dans `~/.dotnet`, ajouté au `PATH`.
  3. `dotnet` plante avec `Couldn't find a valid ICU package` (pas de `libicu`, et `sudo` indisponible sans terminal interactif dans cette session). Contournement : `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` — c'est déjà le mode du conteneur de production (cf. commentaire dans `Program.cs`), donc pas un hack propre au dev.
  4. Outillage EF Core : manifeste d'outils local (`.config/dotnet-tools.json`, `dotnet-ef` épinglé) — `dotnet tool restore` avant `dotnet ef migrations add ...`.
- **Tailscale** relie les postes et le Pi. Tailnet `piranha-pollux.ts.net`, le Pi est le nœud **`mcm8`** — donc `mcm8.piranha-pollux.ts.net`. C'est cette adresse que vise le MCP du lot 3, jamais `raspberrypi.local`. Un second nœud Linux, `proxy`, existe (rôle non documenté ici). Le CLI `tailscale` n'est pas installé dans `Ubuntu-perso` ; utiliser le binaire Windows via l'interop WSL : `"/mnt/c/Program Files/Tailscale/tailscale.exe" status`.
- ⚠️ **Piège vécu, à ne pas repayer** : Tailscale fait expirer les clés de nœud au bout de quelques mois. Le nœud quitte alors le tailnet **sans panne et sans message** — c'est ce qui avait rendu `mcm8` et `proxy` invisibles le 2026-08-04. **`mcm8` est de nouveau en ligne** (vérifié en session), mais désactiver l'expiration de clé sur tout nœud serveur, dans la console d'admin Tailscale, reste à faire pour ne pas revivre l'épisode.
- ⚠️ **Piège vécu au provisionnement Postgres (PR 1)** : un client SQL graphique (DBeaver...) garde un script lié à la base active au moment de son ouverture — un `GRANT ... ON SCHEMA public` lancé dans un onglet resté sur `postgres` s'applique silencieusement au mauvais `public` (0 erreur, mais aucun effet sur `loreai`). Vérifier `SELECT current_database();` avant tout GRANT. Cause du même effet observé deux fois : PostgreSQL 15+ retire `CREATE` à `PUBLIC` sur le schéma `public` par défaut — sans le `GRANT` explicite (déjà dans `docs/deploiement-raspberry-pi.md` §3), EF Core échoue avec `permission denied for schema public` à la première migration.

## Comment tenir ce fichier

En fin de session : « mets à jour l'état des lieux ». Trois lignes changent, rarement plus.

Ne **pas** y mettre : ce qui est fait (c'est `git log`), pourquoi une décision a été prise (c'est un ADR), ni le détail d'un lot (c'est son issue).
