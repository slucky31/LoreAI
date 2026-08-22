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
| **Bloqué par** | **1.** Le Pi est hors ligne (nœud du tailnet, plus vu depuis 17 jours) — rien n'est joignable. **2.** L'instance PostgreSQL n'est pas encore déployée dessus. **3.** Docker est absent du poste de travail et sa faisabilité n'est pas tranchée. Les trois sont des préalables **manuels**, hors dépôt. |

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
- **Docker sur le Shadow PC** — `wsl --install -d Ubuntu` répond en 5 min. Si la virtualisation imbriquée passe, Docker Desktop et Testcontainers suivent ; sinon on bascule sur une base `loreai_dev` (sur le Pi, via Tailscale) ou un PostgreSQL natif Windows, avec isolation par base jetable par exécution. **C'est le préalable à la PR 1 du lot 0.**

## Environnement, à ne pas re-découvrir

- **Poste de travail : un Shadow PC** (Windows hébergé dans le cloud), **pas sur le LAN domestique**. C'est ce qui a invalidé la formulation d'origine de D2 — voir [ADR 0010](adr/0010-topologie-reseau-tailscale.md).
- **Tailscale** relie le poste et le Pi. Les services privés s'adressent par leur nom MagicDNS, jamais par `raspberrypi.local`.
- **Pas de Docker** sur le poste à ce jour.

## Comment tenir ce fichier

En fin de session : « mets à jour l'état des lieux ». Trois lignes changent, rarement plus.

Ne **pas** y mettre : ce qui est fait (c'est `git log`), pourquoi une décision a été prise (c'est un ADR), ni le détail d'un lot (c'est son issue).
