# État des lieux

> Fichier court, **réécrit et jamais complété**. Il répond à une seule question : *où on en est, là, maintenant.*
> L'historique est dans `git log` et les Releases. La cible est dans [`roadmap-phase-3.md`](roadmap-phase-3.md) (phases 1-2 : [`roadmap.md`](roadmap.md)). Les décisions sont dans [`adr/`](adr/).
> Ce qui ne va pas, avec les preuves : [`critique-fonctionnelle.md`](critique-fonctionnelle.md). Ce qui n'a jamais été vérifié en conditions réelles : [`reste-a-tester.md`](reste-a-tester.md).

**Dernière mise à jour :** 2026-08-30 · **Version publiée :** 0.20.0

---

## Où on en est

| | |
|---|---|
| **Lot en cours** | **Aucun.** Les lots 0 → 9 sont livrés : la roadmap des phases 1 et 2 est épuisée, hors lot 10 (déduplication, [#51](https://github.com/slucky31/LoreAI/issues/51)). Une revue fonctionnelle complète a été faite le 2026-08-30 ([`critique-fonctionnelle.md`](critique-fonctionnelle.md)) et a produit une **phase 3** ([`roadmap-phase-3.md`](roadmap-phase-3.md)) : lots 11 (exploitation), 12 (coût), 13 (écritures externes), puis 10 (dédup), 14 (boucle de retour), 15 (second cerveau). |
| **Prochain geste** | Ouvrir le **lot 11 — Reprendre la main sur l'exploitation** : sauvegarde `pg_dump` + `.env` chiffré ([#37](https://github.com/slucky31/LoreAI/issues/37)), journal d'identité au démarrage étendu aux jobs planifiés ([#65](https://github.com/slucky31/LoreAI/issues/65)), healthcheck MCP ([#68](https://github.com/slucky31/LoreAI/issues/68)), Renovate débloqué ([#40](https://github.com/slucky31/LoreAI/issues/40)), logs `--add-watch-topic` ([#97](https://github.com/slucky31/LoreAI/issues/97)). En même temps : fermer les 5 issues livrées (#31, #35, #71, #73, #75) et les 3 périmées (#33, #36, #39). |
| **Dernières décisions** (2026-08-30) | **D8** budget LLM 10 €/mois **avec garde-fou dur** dans le code (lot 12). **D9** newsletters Gmail réinjectées dans Raindrop **sous seuil** ([#94](https://github.com/slucky31/LoreAI/issues/94), lot 13). **D10** l'alerte Discord immédiate `ATester`/`Haute` est **retirée** ([#64](https://github.com/slucky31/LoreAI/issues/64), lot 12). **D11** la classification des flux RSS personnels est **retirée** ([#99](https://github.com/slucky31/LoreAI/issues/99), lot 12) — Miniflux reste lecteur humain + moteur de la veille. Détail dans [`roadmap-phase-3.md`](roadmap-phase-3.md#décisions-actées-session-du-2026-08-30). |
| **À valider** | La règle **« un lot n'est fini que s'il est actif en production »** — proposée pour fermer l'écart livré/actif (4 des 6 derniers lots sont désactivés par défaut, état réel inconnu). Elle ralentit délibérément la livraison. |
| **Bloqué par** | Rien. Le Pi (`mcm8`) est en ligne, le corpus est indexé (1 369 items, index frais du 2026-08-30 12:26 UTC). |

## Ce qui tourne aujourd'hui

**Sur `main` :** le worker classe « Non trié » toutes les 15 min (Claude Haiku, tool-use forcé), applique tags et déplacements dans Raindrop, et envoie un compte-rendu Discord en fin de cycle dès qu'au moins un article a été traité. Persistance PostgreSQL mutualisée (EF Core, ADR 0009/0011), modèle `Item` générique multi-sources (ADR 0012), journal `CycleRuns` et healthcheck Docker. Serveur MCP en lecture seule (ADR 0014), 11 outils, recherche plein texte `tsvector`. Jobs planifiés : indexation de bibliothèque (hebdo), insights hebdomadaires (embeds Discord), revue mensuelle narrative (pièce jointe `.md`), réconciliation (quotidienne). Connecteurs Gmail et RSS/Miniflux, file de lecture taguée et veille sur sujets : **écrits, testés, désactivés par défaut**. Déploiement par image `ghcr.io` multi-arch, le Pi ne fait que `pull`.

**Réellement actif sur `mcm8` : partiellement vérifié.** `Worker__EmailIngestionEnabled` **est actif** (constaté le 2026-08-30 : une entrée `sourceType = Newsletter` du jour même dans la file de lecture). Les trois autres flags (`Worker__FeedIngestionEnabled`, `Worker__ReadingQueueTaggingEnabled`, `Worker__TopicWatchEnabled`) restent inconnus — voir [`reste-a-tester.md`](reste-a-tester.md#-flags-dactivation--les-trois-inconnues-restantes) pour les trois requêtes qui les lèvent. C'est le trou que ferme le lot 11 ([#65](https://github.com/slucky31/LoreAI/issues/65) étendu au listing des jobs planifiés) — d'ici là, **se fier au `.env` sur `mcm8`, jamais à cette ligne**.

## Les trois choses qui doivent changer en premier

Issues de la revue du 2026-08-30, par ordre de gravité — le détail et les preuves sont dans [`critique-fonctionnelle.md`](critique-fonctionnelle.md).

1. **Il n'y a aucune sauvegarde.** Ni `pg_dump` de la base (1 369 items, historique de classification, curseurs — dont la perte déclenche un backfill LLM), ni copie du `.env` de `mcm8` (7 secrets qui n'existent nulle part ailleurs, dont un refresh token OAuth Google). Risque le plus élevé du projet, devant le coût.
2. **Le coût LLM n'est mesuré qu'a posteriori, une fois par semaine.** Aucun code ne peut refuser un appel parce que le budget est dépassé — c'est ce qui a laissé passer le volet RSS du lot 7 à ~36 $/mois estimés (D11/#99), découvert par lecture de code.
3. **Renovate est bloqué** par une virgule manquante dans `renovate.json:5` : aucune mise à jour de dépendance ne passe. Correctif d'un caractère.

## Décisions actées, non encore appliquées

| | Décision | Où c'est écrit |
|---|---|---|
| D1 | Hub multi-sources (Raindrop + Gmail + RSS) | [roadmap](roadmap.md), [ADR 0012](adr/0012-modele-item-generique-multi-sources.md) ✅ — les trois connecteurs sont écrits ; le volet classification RSS est retiré par D11 |
| D2 | Réseau privé strict — LAN **ou** tailnet, jamais d'exposition publique | [ADR 0010](adr/0010-topologie-reseau-tailscale.md) ✅ |
| D4 | Le vault Obsidian n'est pas visible du Pi | [roadmap](roadmap.md) — conditionne S12 (export côté PC, lot 15) |
| D5 | On récupère le contenu réel des articles | [roadmap](roadmap.md) ✅ (lot 4) |
| D6 | Modèle de synthèse non tranché | [roadmap](roadmap.md) |
| D7 | PostgreSQL mutualisé, EF Core (pas Dapper) | [ADR 0009](adr/0009-postgresql-mutualise-sur-le-pi.md) ✅, [ADR 0011](adr/0011-ef-core-remplace-dapper.md) ✅ |
| D8 | Budget LLM 10 €/mois avec garde-fou dur | [phase 3](roadmap-phase-3.md) — lot 12 |
| D9 | Newsletters Gmail → Raindrop sous seuil | [phase 3](roadmap-phase-3.md), [#94](https://github.com/slucky31/LoreAI/issues/94) — lot 13 |
| D10 | Retrait de l'alerte Discord immédiate | [phase 3](roadmap-phase-3.md), [#64](https://github.com/slucky31/LoreAI/issues/64) — lot 12 |
| D11 | Retrait de la classification des flux RSS personnels | [phase 3](roadmap-phase-3.md), [#99](https://github.com/slucky31/LoreAI/issues/99) — lot 12 |

## À trancher par une mesure, pas par un débat

- **Cache de prompt** ([#34](https://github.com/slucky31/LoreAI/issues/34)) — la mesure de 30 min annoncée depuis la phase 1 n'a jamais été faite. Elle devient une simple lecture une fois la table `LlmCalls` en place (lot 12). **À trancher et fermer dans ce lot.**
- **Utilité du lot 10 (déduplication)** — combien de doublons inter-sources le rapport hebdomadaire remonte-t-il réellement ? Avec D11, il ne reste que trois producteurs d'items classés.
- **`important` / `broken`** — 0 sur 1 369 items. Retirer l'analyseur de liens morts, ou sonder réellement les URLs (le crawler que N3 voulait éviter) ?
- **`pgvector`** — conditionné à l'échec **mesuré** de l'expansion de requête (S11, lot 15), jamais ouvert par anticipation.
- **Autoheal** — laissé sans ticket : aucun blocage observé en trois mois. `CycleRuns` existe désormais pour le mesurer, à reconsidérer sur données.
- ~~**Spike OpenClaw**~~ — tranché 2026-08-23 : c'est un *client* MCP, pas un serveur. Détail dans [roadmap.md](roadmap.md#un-outil-existe-t-il-déjà--spike-fait-2026-08-23--le-lot-3-reste-nécessaire).
- ~~**Docker sur le poste de travail**~~ — tranché : le Shadow PC ne peut pas faire tourner Docker (`HCS_E_HYPERV_NOT_INSTALLED`).

## Environnement, à ne pas re-découvrir

- **Deux postes.** `shadow-p9t9qc7h` (**Shadow PC**, Windows hébergé dans le cloud) et **`afl-it-ndu`**, la machine de développement Docker-capable — dessus, le travail se fait dans la distro WSL2 **`Ubuntu-perso`**. Aucun des deux n'est sur le LAN domestique — c'est ce constat qui a invalidé la formulation d'origine de D2, voir [ADR 0010](adr/0010-topologie-reseau-tailscale.md).
- **Le développement et les tests se font depuis `Ubuntu-perso` (sur `afl-it-ndu`)**, puisque `Testcontainers.PostgreSql` exige un daemon Docker. Le Shadow reste utilisable pour le reste : lecture, rédaction, `git`, appels d'API.
- **Le Shadow ne pourra jamais exécuter la suite de tests** : sa plateforme n'expose pas la virtualisation imbriquée (`HCS_E_HYPERV_NOT_INSTALLED`), donc ni WSL2, ni Hyper-V, ni Docker Desktop. Inutile de réessayer.
- **`Ubuntu-perso` a eu besoin d'une mise en route, désormais faite mais pas automatisée — utile si un jour reprovisionnée :**
  1. Docker Desktop (Windows) tournait déjà sur `afl-it-ndu` mais son intégration WSL doit être activée **par distro** — `Settings → Resources → WSL Integration`, cocher `Ubuntu-perso`, puis rouvrir un terminal (le shell déjà ouvert ne voit pas le changement).
  2. Le SDK .NET n'y était **pas préinstallé**. Installé via `dotnet-install.sh --version <celle de global.json>` dans `~/.dotnet`, ajouté au `PATH`.
  3. `dotnet` plante avec `Couldn't find a valid ICU package` (pas de `libicu`). Contournement : `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` — c'est déjà le mode du conteneur de production, donc pas un hack propre au dev.
  4. Outillage EF Core : manifeste d'outils local (`.config/dotnet-tools.json`, `dotnet-ef` épinglé) — `dotnet tool restore` avant `dotnet ef migrations add ...`.
- **Tailscale** relie les postes et le Pi. Tailnet `piranha-pollux.ts.net`, le Pi est le nœud **`mcm8`** — donc `mcm8.piranha-pollux.ts.net`. C'est cette adresse que vise le MCP, jamais `raspberrypi.local`. Le CLI `tailscale` n'est pas installé dans `Ubuntu-perso` ; utiliser le binaire Windows via l'interop WSL : `"/mnt/c/Program Files/Tailscale/tailscale.exe" status`.
- ⚠️ **Piège vécu, à ne pas repayer** : Tailscale fait expirer les clés de nœud au bout de quelques mois. Le nœud quitte alors le tailnet **sans panne et sans message** — c'est ce qui avait rendu `mcm8` et `proxy` invisibles le 2026-08-04. **`mcm8` est en ligne**, mais désactiver l'expiration de clé sur tout nœud serveur, dans la console d'admin Tailscale, **reste à faire**.
- ⚠️ **Piège vécu au provisionnement Postgres** : un client SQL graphique garde un script lié à la base active au moment de son ouverture — un `GRANT ... ON SCHEMA public` lancé dans un onglet resté sur `postgres` s'applique silencieusement au mauvais `public`. Vérifier `SELECT current_database();` avant tout GRANT. PostgreSQL 15+ retire `CREATE` à `PUBLIC` sur le schéma `public` par défaut — sans le `GRANT` explicite (déjà dans `docs/deploiement-raspberry-pi.md` §3), EF Core échoue avec `permission denied for schema public`.

## Comment tenir ce fichier

En fin de session : « mets à jour l'état des lieux ». Trois lignes changent, rarement plus.

Ne **pas** y mettre : ce qui est fait (c'est `git log`), pourquoi une décision a été prise (c'est un ADR), ni le détail d'un lot (c'est son issue).
