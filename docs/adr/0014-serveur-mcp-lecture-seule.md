# 0014 — Quatrième projet : serveur MCP en lecture seule

## Statut
Acceptée — précise l'[ADR 0001](0001-architecture-generale.md) (le nombre de projets, pas la logique interfaces/implémentations, qui reste valide) et s'appuie sur l'[ADR 0010](0010-topologie-reseau-tailscale.md) (réseau) et l'[ADR 0009](0009-postgresql-mutualise-sur-le-pi.md) (rôle `loreai_ro`).

## Contexte

Le lot 3 ([#44](https://github.com/slucky31/LoreAI/issues/44)) expose le corpus LoreAI en lecture via le [Model Context Protocol](https://modelcontextprotocol.io/) : `search_items`, `get_item`, `list_recent`, `stats`, `find_similar`, `reading_queue`, `list_tools`. Avec la disparition du canal email (D3, [ADR 0013](0013-retrait-canal-email.md)), c'est le **canal principal** d'interrogation à la demande — sans qu'aucune interface ne soit à écrire : Claude Code (ou tout autre client MCP) devient le front.

L'issue #44 pose deux garde-fous avant d'écrire du code, dont l'un explicitement : un ADR, faute de quoi le sujet reprendrait la même dérive que D1 (multi-sources) — un numéro pré-attribué (« ADR 0011 ») s'est retrouvé pris par [EF Core](0011-ef-core-remplace-dapper.md) entre-temps. Le second — un spike sur [OpenClaw](https://github.com/openclaw/openclaw) pour vérifier qu'aucun outil existant ne couvre déjà ce périmètre — a été mené le 2026-08-23 (détail dans [`roadmap.md`](../roadmap.md#un-outil-existe-t-il-déjà--spike-fait-2026-08-23--le-lot-3-reste-nécessaire)) : OpenClaw est un *client* MCP, pas un serveur, il ne peut donc pas se substituer au travail décrit ici.

Deux questions structurelles restaient ouvertes avant le code :

1. **Faut-il un quatrième projet**, ou le serveur MCP peut-il vivre dans `LoreAI.Worker` existant ?
2. **Comment ce projet touche-t-il le réseau et la base**, sans réouvrir les invariants déjà actés (`Core` sans dépendance externe, réseau privé strict, pas d'écriture hors « Non trié ») ?

## Décision

### Un projet séparé, pas un mode du Worker

`src/LoreAI.Mcp` devient un **quatrième projet**, avec son propre `Program.cs`/hôte et son propre exécutable — pas un endpoint ajouté à `LoreAI.Worker`, ni un mode `--mcp` du même binaire.

Raisons :

- **Surface réseau distincte.** Le Worker n'écoute aujourd'hui sur aucun port (jobs planifiés + sonde `--health-check` en exec, jamais de serveur HTTP). Lui ajouter un port ouvert sur le tailnet mélangerait deux profils de risque très différents dans le même processus : une panne ou une faille du serveur MCP ne doit pas pouvoir affecter le pipeline de classification, et inversement.
- **Cycle de vie et déploiement indépendants.** Le MCP doit pouvoir redémarrer, être mis à jour ou tomber sans interrompre le polling toutes les 15 minutes. Deux conteneurs Compose distincts (`loreai` et `loreai-mcp`) rendent ça gratuit ; un seul binaire à deux visages l'interdirait.
- **Rôle PostgreSQL différent par construction.** Le Worker se connecte avec le rôle propriétaire (lecture/écriture) ; le MCP se connecte avec `loreai_ro` (`GRANT SELECT` uniquement, [ADR 0009](0009-postgresql-mutualise-sur-le-pi.md)). Deux chaînes de connexion différentes pour le même processus serait une source d'erreur ; deux projets avec chacun sa propre configuration l'élimine structurellement — impossible d'écrire par erreur depuis le code du MCP, la base le refuserait même en cas de bug.
- **Image chiselée sans shell.** `runtime:10.0-noble-chiseled` (déjà en production pour le Worker) n'a ni shell ni `docker exec`. Un second processus dans le même conteneur exigerait un supervisor (`tini` multi-process, `s6`...) — complexité rejetée pour une simple séparation de préoccupations. Un conteneur dédié avec son propre `ENTRYPOINT` est plus simple, pas plus complexe.

Ce que ça coûte : un `.csproj` de plus, une image Docker de plus à construire et publier sur `ghcr.io`, une entrée de plus dans `docker-compose.yml`. Jugé proportionné vu ce qui précède — et cohérent avec le principe déjà énoncé par l'[ADR 0001](0001-architecture-generale.md) : découper selon les responsabilités réelles, pas par anticipation.

### Dépendance : `Mcp → Infrastructure → Core`, jamais l'inverse

`LoreAI.Mcp` référence `LoreAI.Infrastructure` (accès EF Core au `DbContext`, en lecture) et `LoreAI.Core` (modèles), exactement comme `LoreAI.Worker` aujourd'hui. Il n'introduit **aucune** nouvelle dépendance dans `Core` : le SDK `ModelContextProtocol` (transport Streamable HTTP) reste confiné à `LoreAI.Mcp`, au même titre que `Npgsql`/EF Core sont confinés à `Infrastructure`. L'invariant « `Core` à zéro dépendance externe » de l'[ADR 0001](0001-architecture-generale.md) n'est donc pas rouvert par ce projet, seulement élargi d'un consommateur de plus.

Les sept outils MCP sont de fines façades sur les méthodes déjà prévues d'`IArticleRepository` (`GetByIdAsync`, `GetByFilterAsync`, `SearchAsync`, `CountByAsync`, [ADR 0012](0012-modele-item-generique-multi-sources.md)) — aucune logique métier nouvelle dans `LoreAI.Mcp` lui-même, qui reste une couche de présentation du protocole.

### Réseau et sécurité : défense en profondeur, rien de nouveau à trancher

L'[ADR 0010](0010-topologie-reseau-tailscale.md) a déjà tranché le principe pour ce projet nommément — cet ADR n'y ajoute rien, il l'applique :

- Le conteneur `loreai-mcp` écoute sur l'interface Tailscale (nom MagicDNS `mcm8.piranha-pollux.ts.net`), jamais sur `0.0.0.0` redirigé vers la box.
- Le rôle `loreai_ro` (`GRANT SELECT`, déjà provisionné par [ADR 0009](0009-postgresql-mutualise-sur-le-pi.md)) est la garantie de non-écriture au niveau base, indépendante du réseau.
- Un token bearer reste obligatoire malgré le réseau privé — défense en profondeur, pas redondance : le réseau limite qui frappe à la porte, la base garantit qu'on ne peut rien casser même en cas d'accès non autorisé.
- Transport **Streamable HTTP** (pas stdio) : nécessaire pour qu'un client distant sur le tailnet (le poste de travail, hors LAN — [ADR 0010](0010-topologie-reseau-tailscale.md)) puisse s'y connecter. stdio suppose un processus local au client, incompatible avec la topologie réelle.

### Outils en écriture (Q3) explicitement hors scope

Ce lot et cet ADR ne couvrent que la lecture. Toute future capacité d'écriture MCP (« marque comme lu », « range dans X », Q3 de la roadmap) est un changement d'invariant qui exigera sa propre décision — un rôle en écriture, une revue de sécurité distincte — pas une extension silencieuse de `loreai_ro`.

## Alternatives écartées

- **Endpoint HTTP ajouté à `LoreAI.Worker`.** Écarté : mélange deux profils de risque et deux rôles PostgreSQL dans un seul processus (voir ci-dessus).
- **Un seul conteneur, deux processus supervisés.** Écarté : réintroduit la nécessité d'un shell/supervisor dans une image volontairement chiselée, pour économiser une entrée `docker-compose.yml`.
- **Transport stdio.** Écarté : suppose un client colocalisé avec le serveur, ce que la topologie réelle (poste de travail hors LAN, [ADR 0010](0010-topologie-reseau-tailscale.md)) exclut.
- **Rôle PostgreSQL partagé avec le Worker.** Écarté : annulerait la seule garantie structurelle de non-écriture du MCP, qui redeviendrait une simple convention de code plutôt qu'une contrainte imposée par la base.
- **OpenClaw à la place du serveur MCP.** Écarté par le spike du 2026-08-23 : OpenClaw est un client MCP, pas un serveur — il faudrait de toute façon écrire ce que cet ADR décrit.

## Conséquences

- Nouveau `src/LoreAI.Mcp.csproj`, nouvelle image `ghcr.io`, nouvelle entrée `docker-compose.yml` (`loreai-mcp`, port publié uniquement sur l'interface Tailscale), nouvelle variable d'environnement `Postgres__ConnectionString` pointant `loreai_ro` plutôt que le rôle propriétaire.
- CI/CD : le workflow `docker` doit builder et publier une seconde image ; `cd.yml` reste piloté par un seul `<Version>` dans `Directory.Build.props` ([ADR 0008](0008-versioning-semver-conventional-commits.md)) — les deux projets restent versionnés ensemble, pas de version indépendante pour le MCP.
- `README.md` et `.mcp.json` côté poste de développement documentent l'URL (`http://mcm8.piranha-pollux.ts.net:5099/mcp`) et le token bearer requis.
- Prérequis d'exploitation hérité de l'[ADR 0010](0010-topologie-reseau-tailscale.md), rappelé ici parce que ce projet en dépend directement : `mcm8` doit rester un nœud en ligne du tailnet, expiration de clé désactivée.
- Débloque **L6** (recoupement avec le vault Obsidian) sans code supplémentaire : le vault reste local au poste de travail, le MCP expose le corpus sur le tailnet, le recoupement se fait côté client.
