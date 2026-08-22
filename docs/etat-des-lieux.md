# État des lieux

> Fichier court, **réécrit et jamais complété**. Il répond à une seule question : *où on en est, là, maintenant.*
> L'historique est dans `git log` et les Releases. La cible est dans [`roadmap.md`](roadmap.md). Les décisions sont dans [`adr/`](adr/).
> Le suivi des lots est dans les [issues #41 à #51](https://github.com/slucky31/LoreAI/issues?q=is%3Aissue+milestone%3A*).

**Dernière mise à jour :** 2026-08-22 · **Version publiée :** 0.4.1

---

## Où on en est

| | |
|---|---|
| **Lot en cours** | Aucun. La phase de cadrage vient de se terminer. |
| **Prochain geste** | Lot 0 ([#41](https://github.com/slucky31/LoreAI/issues/41)), **PR 1** : socle PostgreSQL à schéma fonctionnellement constant. |
| **Dernière décision** | **D7** — persistance sur PostgreSQL mutualisé auto-hébergé ([ADR 0009](adr/0009-postgresql-mutualise-sur-le-pi.md), remplace l'ADR 0002). |
| **Bloqué par** | **1.** Le Pi (`mcm8`) est hors du tailnet : **clé de nœud expirée**, pas une panne — la machine tourne probablement. À ré-authentifier (`sudo tailscale up`), puis **désactiver l'expiration de clé**. **2.** L'instance PostgreSQL n'est pas encore déployée dessus. Les deux sont des préalables **manuels**, hors dépôt. |

## Ce qui tourne aujourd'hui

Le worker classe la collection « Non trié » toutes les 15 min (Claude Haiku, tool-use forcé), applique tags et déplacements dans Raindrop, alerte sur Discord les articles `ATester` / `Haute`, et envoie un digest email quotidien. Persistance SQLite sur le Pi. Déploiement par image `ghcr.io` multi-arch, le Pi ne fait que `pull`.

**Rien de la roadmap n'est encore implémenté.** La base reste en écriture seule.

## Décisions actées, non encore appliquées

| | Décision | Où c'est écrit |
|---|---|---|
| D1 | Hub multi-sources (Raindrop + Gmail + RSS) | [roadmap](roadmap.md), ADR 0010 **à écrire** |
| D2 | Réseau privé strict — LAN **ou tailnet**, jamais d'exposition publique | [ADR 0010](adr/0010-topologie-reseau-tailscale.md) ✅ |
| D3 | L'email disparaît complètement | [roadmap](roadmap.md), rouvre l'ADR 0005 |
| D4 | Le vault Obsidian n'est pas visible du Pi | [roadmap](roadmap.md) |
| D5 | On récupère le contenu réel des articles | [roadmap](roadmap.md) |
| D6 | Modèle de synthèse non tranché | [roadmap](roadmap.md) |
| D7 | PostgreSQL mutualisé | [ADR 0009](adr/0009-postgresql-mutualise-sur-le-pi.md) ✅ |

## À trancher par une mesure, pas par un débat

- **Spike OpenClaw** — une demi-journée, **avant** le lot 3 ([#44](https://github.com/slucky31/LoreAI/issues/44)). C'est le premier lot qui construit une surface que cet outil pourrait déjà fournir.
- **Cache de prompt** ([#34](https://github.com/slucky31/LoreAI/issues/34)) — 30 min de mesure, à faire au lot 4. A priori sans effet au volume actuel, décisif au backfill.
- ~~**Docker sur le poste de travail**~~ — **tranché.** Le Shadow PC ne peut pas faire tourner Docker (`HCS_E_HYPERV_NOT_INSTALLED` : pas de virtualisation imbriquée). Le développement se fait donc depuis un environnement qui en dispose, et les tests utilisent `Testcontainers.PostgreSql`.

## Environnement, à ne pas re-découvrir

- **Deux postes.** Un **Shadow PC** (Windows hébergé dans le cloud) et un second environnement disposant de Docker. Aucun des deux n'est supposé être sur le LAN domestique — c'est ce constat qui a invalidé la formulation d'origine de D2, voir [ADR 0010](adr/0010-topologie-reseau-tailscale.md).
- **Le développement et les tests se font depuis l'environnement doté de Docker**, puisque `Testcontainers.PostgreSql` l'exige. Le Shadow reste utilisable pour le reste : lecture, rédaction, `git`, appels d'API.
- **Le Shadow ne pourra jamais exécuter la suite de tests** : sa plateforme n'expose pas la virtualisation imbriquée (`HCS_E_HYPERV_NOT_INSTALLED`), donc ni WSL2, ni Hyper-V, ni Docker Desktop. Inutile de réessayer.
- **Tailscale** relie les postes et le Pi. Tailnet `piranha-pollux.ts.net`, le Pi est le nœud **`mcm8`** — donc `mcm8.piranha-pollux.ts.net`. C'est cette adresse que vise le MCP du lot 3, jamais `raspberrypi.local`. Un second nœud Linux, `proxy`, existe (rôle non documenté ici).
- ⚠️ **Piège vécu, à ne pas repayer** : Tailscale fait expirer les clés de nœud au bout de quelques mois. Le nœud quitte alors le tailnet **sans panne et sans message** — c'est ce qui a rendu `mcm8` et `proxy` invisibles le 2026-08-04. Désactiver l'expiration de clé sur tout nœud serveur, dans la console d'admin.
- ⚠️ À vérifier depuis le nouveau poste : qu'il soit bien sur le tailnet.

## Comment tenir ce fichier

En fin de session : « mets à jour l'état des lieux ». Trois lignes changent, rarement plus.

Ne **pas** y mettre : ce qui est fait (c'est `git log`), pourquoi une décision a été prise (c'est un ADR), ni le détail d'un lot (c'est son issue).
