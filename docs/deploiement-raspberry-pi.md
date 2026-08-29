# Déploiement sur Raspberry Pi (de zéro)

Ce guide part d'un Raspberry Pi 64-bit fraîchement installé (Raspberry Pi OS Lite 64-bit, accès SSH déjà fonctionnel) et couvre toutes les étapes jusqu'au worker qui tourne. Rien n'est compilé sur le Pi : l'image est construite et publiée par la CD GitHub sur `ghcr.io`, en multi-arch (`linux/amd64` + `linux/arm64`) — voir [README.md](../README.md#déploiement-sur-raspberry-pi) pour la version courte.

Toutes les commandes Docker sont préfixées par `sudo` (pas d'ajout de l'utilisateur au groupe `docker`).

## 1. Vérifier le système

```bash
uname -m                    # doit renvoyer aarch64 (Raspberry Pi OS 64-bit)
sudo apt update && sudo apt full-upgrade -y
```

## 2. Installer Docker

```bash
curl -fsSL https://get.docker.com -o get-docker.sh
sudo sh get-docker.sh
rm get-docker.sh

sudo docker --version
sudo docker compose version
```

Le script officiel installe Docker Engine + le plugin Compose (`docker compose`, sans tiret) sur les architectures arm64.

## 3. Provisionner la base PostgreSQL

L'instance PostgreSQL mutualisée ([ADR 0009](adr/0009-postgresql-mutualise-sur-le-pi.md)) est un composant d'infrastructure de la machine, déployé par sa propre stack Compose — hors de ce guide, qui suppose qu'elle tourne déjà. LoreAI a besoin d'y créer sa propre base et ses propres rôles, une seule fois.

Connectez-vous en tant que superutilisateur de l'instance (`psql`, depuis le Pi ou un poste qui l'atteint) et exécutez, **dans cet ordre, en deux temps** :

**1. Base et rôles** — dans n'importe quelle base (les rôles sont globaux au cluster, pas à une base) :

```sql
CREATE DATABASE loreai;
CREATE ROLE loreai LOGIN PASSWORD '<mot_de_passe_a_choisir>';
CREATE ROLE loreai_ro LOGIN PASSWORD '<mot_de_passe_a_choisir>';
GRANT CONNECT ON DATABASE loreai TO loreai_ro;
```

**2. Droits sur le schéma** — `public` est propre à chaque base : il faut être **connecté à `loreai`** pour que ces commandes visent la bonne base.

```sql
GRANT ALL ON SCHEMA public TO loreai;
GRANT USAGE ON SCHEMA public TO loreai_ro;
-- Les migrations EF Core (lancees par le worker, role "loreai") creent les tables apres coup :
-- ALTER DEFAULT PRIVILEGES garantit que loreai_ro les voit sans regrant manuel a chaque migration.
ALTER DEFAULT PRIVILEGES FOR ROLE loreai IN SCHEMA public GRANT SELECT ON TABLES TO loreai_ro;
```

Sous `psql`, changez de base entre les deux blocs avec `\c loreai`. ⚠️ `\c` est une commande **`psql`**, pas du SQL standard : un client graphique (DBeaver, pgAdmin, DataGrip...) ne la comprend pas et interrompt généralement le script à cette ligne — d'où l'utilité de garder ce bloc séparé, à exécuter après avoir changé de base **depuis l'interface du client** plutôt que via `\c`.

`loreai_ro` (lecture seule) n'est pas utilisé avant le lot 3 (serveur MCP) mais créé maintenant pour ne pas rejouer cette étape plus tard. Le mot de passe du rôle `loreai` va dans `Postgres__ConnectionString`, à l'étape 5.

Vérifiez ensuite que LoreAI pourra rejoindre le réseau Docker externe de l'instance (`pi-postgres` par défaut, voir `docker-compose.yml`) :

```bash
sudo docker network ls | grep pi-postgres
```

S'il n'existe pas encore sous ce nom, créez-le (`sudo docker network create pi-postgres`) et rattachez-y le conteneur Postgres de la stack mutualisée, ou ajustez le nom dans `docker-compose.yml` pour qu'il corresponde au réseau réel.

## 4. Récupérer les secrets

| Secret | Où le récupérer |
|---|---|
| `Raindrop__Token` | Raindrop.io → **Settings → Integrations → App Management Console → For Developers → Create new app** → dans l'app créée, onglet **Test token** (ne pas passer par OAuth, ce token n'expire pas) |
| `Classifier__ApiKey` | [console.anthropic.com](https://console.anthropic.com) → **API Keys → Create Key** |
| `Discord__WebhookUrl` | Discord → paramètres du salon voulu → **Intégrations → Webhooks → Nouveau webhook** → copier l'URL |

## 5. Récupérer les fichiers de déploiement

Pas besoin de cloner le repo entier : seuls `docker-compose.yml` et `.env` sont nécessaires.

```bash
mkdir -p ~/loreai && cd ~/loreai
curl -O https://raw.githubusercontent.com/slucky31/LoreAI/main/docker-compose.yml
curl -O https://raw.githubusercontent.com/slucky31/LoreAI/main/.env.example
cp .env.example .env
```

## 6. Configurer le `.env`

```bash
nano .env
```

Renseigner les secrets récupérés à l'étape 4, et la chaîne de connexion Postgres provisionnée à l'étape 3 (`Postgres__ConnectionString`). Points d'attention :

- `Raindrop__CollectionId` doit rester à `-1` (« Non trié ») — `0` viserait toute la bibliothèque, y compris ce qui est déjà rangé.
- Laisser `Worker__WriteBackToRaindrop=false` au premier lancement, pour observer les suggestions dans les logs sans rien modifier dans Raindrop. Repasser à `true` une fois rassuré.
- `LOREAI_TAG` (optionnel, dans `.env`) permet d'épingler une version précise (ex. `0.3.5`) plutôt que de suivre `latest`.

## 7. Préparer le dossier de logs

```bash
mkdir -p data
sudo chown -R 1654:1654 data
```

Le conteneur tourne en non-root (uid 1654, image « chiselée » sans shell). Sur un bind mount, c'est la propriété côté hôte qui prime : sans ce `chown`, l'écriture des logs échoue avec un `Permission denied` sur `/data`. Ce dossier ne porte plus la base de données depuis la bascule PostgreSQL (ADR 0009) — seulement `logs/`.

## 8. Premier démarrage et limitation du backfill

Sans état de polling préexistant, l'outil remonte **tout l'historique** de « Non trié » dès le premier cycle (pas de webhook natif côté API, cf. [ADR 0003](adr/0003-strategie-polling-raindrop.md)). Si cette collection contient déjà beaucoup d'articles, cela peut déclencher un gros volume d'appels Anthropic et, si `WriteBackToRaindrop=true`, modifier énormément de raindrops d'un coup. Le cycle de polling tourne toutes les 15 min par défaut (`Worker__PollingCronExpression`) : il y a donc une fenêtre pour agir après le tout premier démarrage.

```bash
sudo docker compose pull
sudo docker compose up -d
```

Le premier démarrage applique les migrations EF Core sur la base `loreai` (schéma créé à ce moment-là) avant même le premier cycle de polling. Pour repartir d'un point précis plutôt que de tout l'historique, arrêtez le conteneur tout de suite et seedez `PollingStates` :

```bash
sudo docker compose stop
psql "postgresql://loreai:<mot_de_passe>@<hote-postgres>:5432/loreai"
```

```sql
INSERT INTO "PollingStates" ("SourceType", "LastSourceItemId", "LastCreatedUtc", "UpdatedAtUtc")
VALUES ('Raindrop', '<id_du_dernier_raindrop_a_ignorer>', '<date_ISO8601_UTC>', '<date_ISO8601_UTC>');
```

Puis relancer :

```bash
sudo docker compose up -d
sudo docker compose logs -f
```

Seuls les logs (`/data/logs/`) sont persistés via le volume `./data` : la base de données vit sur l'instance PostgreSQL mutualisée (étape 3), pas dans ce volume.

## 9. Mettre à jour une installation existante

```bash
cd ~/loreai
sudo docker compose pull
sudo docker compose up -d
```

## 10. Dépannage

**`You must install or update .NET to run this application` dans les logs**
L'image publiée ciblait une version de framework .NET différente de celle embarquée dans le runtime du conteneur (bug corrigé en v0.3.5 — voir [PR #26](https://github.com/slucky31/LoreAI/pull/26)). Repasser par l'étape 9 pour récupérer une image à jour.

**`Permission denied` sur `/data` au démarrage**
Le dossier `data/` côté hôte n'appartient pas à l'uid 1654. Refaire l'étape 7 (`sudo chown -R 1654:1654 data`) — nécessaire aussi après mise à jour d'une installation qui tournait auparavant en root.

**Le worker démarre mais rien ne se passe, logs `LogWarning` répétés sur Postgres injoignable**
Conforme à l'ADR 0009 (panne transitoire, non fatale) : vérifiez que le réseau Docker externe `pi-postgres` existe et que le conteneur du worker y est bien rattaché (`sudo docker network inspect pi-postgres`), et que `Postgres__ConnectionString` dans `.env` pointe vers le bon hôte/rôle/mot de passe (étape 3).

**Aucune notification Discord reçue**
Vérifier `Discord__WebhookUrl` dans `.env`, puis `sudo docker compose logs -f` pour l'erreur d'envoi exacte.

## 11. Lot 3 : activer le serveur MCP (#44, ADR 0014)

La CD publie `ghcr.io/slucky31/loreai-mcp` au même rythme et avec la même version que `loreai-worker` (job `docker` de `cd.yml`, deux images construites et taguées ensemble depuis le lot 3) — rien à construire à la main, seulement à renseigner `.env` et démarrer.

**Rien à reprovisionner côté base** : le rôle `loreai_ro` existe déjà depuis l'étape 3 (`GRANT SELECT` via `ALTER DEFAULT PRIVILEGES`, donc à jour sans regrant même après de nouvelles migrations).

1. **Récupérer l'adresse Tailscale du Pi**, depuis `mcm8` lui-même :

   ```bash
   tailscale ip -4
   ```

   Cette IP (jamais `0.0.0.0` ni une IP du LAN — D2/[ADR 0010](adr/0010-topologie-reseau-tailscale.md)) va dans `MCP_TAILSCALE_BIND_IP`.

2. **Générer le jeton bearer** — défense en profondeur derrière le tailnet, pas une seconde ligne de défense optionnelle :

   ```bash
   openssl rand -hex 32
   ```

   Ce jeton va dans `MCP_BEARER_TOKEN`, et devra être reporté dans le `.mcp.json` du poste de développement (étape 4 ci-dessous) — ne le perdez pas.

3. **Compléter `.env`** (les trois clés ajoutées par le lot 3, déjà présentes dans `.env.example`) :

   ```bash
   nano .env
   ```

   - `MCP_POSTGRES_CONNECTION_STRING` : même hôte/port/base que `Postgres__ConnectionString`, mais `Username=loreai_ro` et son propre mot de passe (celui choisi à l'étape 3, pas celui du rôle `loreai`).
   - `MCP_BEARER_TOKEN` : le jeton généré ci-dessus.
   - `MCP_TAILSCALE_BIND_IP` : l'IP obtenue ci-dessus.

4. **Configurer le client MCP** (Claude Code, sur le poste de développement — hors LAN, cf. ADR 0010) : créer ou compléter `.mcp.json` à la racine du projet côté poste de dev, **jamais committé avec le jeton en clair** :

   ```jsonc
   {
     "mcpServers": {
       "loreai": {
         "type": "http",
         "url": "http://<ip-tailscale-de-mcm8>:5099/mcp",
         "headers": { "Authorization": "Bearer <MCP_BEARER_TOKEN>" }
       }
     }
   }
   ```

5. **Démarrer**, une fois l'image publiée :

   ```bash
   sudo docker compose pull
   sudo docker compose up -d loreai-mcp
   sudo docker compose logs -f loreai-mcp
   ```

   `Now listening on: http://[::]:8080` confirme le démarrage ; une requête `initialize` sans jeton doit répondre `401`, avec le bon jeton `200` (voir `tests/LoreAI.Mcp.Tests` pour le comportement attendu du middleware).

## 12. Lot 7 : déployer Miniflux (connecteur RSS, #48)

Miniflux est le moteur d'ingestion RSS (via son API REST, consommée par `MinifluxIngester`) **et** l'interface de lecture humaine (remplace Feedly) — voir [roadmap.md](roadmap.md), section « Miniflux auto-hébergé ». Il tourne dans son propre conteneur (`miniflux`, `docker-compose.yml`), sur sa propre base de l'instance PostgreSQL mutualisée — jamais la base `loreai`.

1. **Provisionner la base et le rôle**, comme à l'étape 3 mais pour Miniflux (superutilisateur de l'instance, dans n'importe quelle base pour la création des rôles) :

   ```sql
   CREATE DATABASE miniflux;
   CREATE ROLE miniflux LOGIN PASSWORD '<mot_de_passe_a_choisir>';
   GRANT ALL PRIVILEGES ON DATABASE miniflux TO miniflux;
   ```

   Puis, **connecté directement à la base `miniflux`** (ex. `psql "postgresql://<superutilisateur>@<hote>:5432/miniflux"` — jamais `\c`, non portable vers un client graphique) :

   ```sql
   GRANT ALL ON SCHEMA public TO miniflux;
   ```

   Miniflux applique ses propres migrations au démarrage (`RUN_MIGRATIONS=1`) : pas de schéma à créer à la main au-delà de ce `GRANT`.

2. **Compléter `.env`** (clés déjà présentes dans `.env.example`) :

   - `MINIFLUX_DATABASE_URL` : `postgres://miniflux:<mot_de_passe>@pg_main:5432/miniflux?sslmode=disable` (même host/réseau que `Postgres__ConnectionString`, base et rôle différents).
   - `MINIFLUX_ADMIN_USERNAME` / `MINIFLUX_ADMIN_PASSWORD` : compte admin créé au premier démarrage (`CREATE_ADMIN=1`), servira à se connecter à l'UI.
   - `MCP_TAILSCALE_BIND_IP` : déjà renseignée si le lot 3 est actif — réutilisée telle quelle (même Pi, même garde-fou D2/ADR 0010 : jamais `0.0.0.0`).

3. **Démarrer et se connecter** :

   ```bash
   sudo docker compose pull
   sudo docker compose up -d miniflux
   sudo docker compose logs -f miniflux
   ```

   Ouvrir `http://<ip-tailscale-de-mcm8>:5100` depuis un poste sur le tailnet (port hôte 5100, cf. `docker-compose.yml` — pas 8080, déjà occupé par un autre conteneur sur `mcm8`), se connecter avec le compte admin, puis **ajouter les flux RSS souhaités** via l'interface (« Add subscription »).

4. **Générer le jeton API** : Settings → API Keys → Create a new API key → reporter dans `Miniflux__ApiToken` (`.env`, section worker). Reporter aussi `Miniflux__BaseUrl=http://miniflux:8080` (DNS interne du réseau Docker partagé, pas l'adresse Tailscale — le worker et Miniflux sont colocalisés).

5. **Seeder le curseur d'entrée**, même logique que le « Premier démarrage » de Raindrop (étape 8) et le connecteur Gmail (README) : jamais de backfill automatique au premier démarrage.

   ```bash
   curl -H "X-Auth-Token: <jeton_genere_a_l_etape_4>" "http://<ip-tailscale-de-mcm8>:5100/v1/entries?order=id&direction=desc&limit=1"
   ```

   Récupérer le champ `id` de l'unique entrée retournée, puis :

   ```sql
   INSERT INTO "PollingStates" ("SourceType", "LastSourceItemId", "LastCreatedUtc", "UpdatedAtUtc")
   VALUES ('Feed', '<id_recupere_ci_dessus>', NULL, '<date_ISO8601_UTC>');
   ```

6. **Activer le connecteur** : `Worker__FeedIngestionEnabled=true` dans `.env`, puis `sudo docker compose up -d loreai-worker` pour redémarrer le worker avec la section `Miniflux__*` désormais validée au démarrage.

## 13. Lot 9 : activer la veille automatique sur sujets (C4, #50)

Réutilise l'instance Miniflux déployée à l'étape 12, mais via une **catégorie dédiée**, strictement séparée des flux de lecture personnelle — voir README, section « Veille automatique sur sujets », pour le détail des variables `Watch__*`.

1. **Créer la catégorie** dans l'UI Miniflux (Settings → Categories, ex. « Veille »), puis y **ajouter les flux RSS de recherche** (Google News RSS `?q=...` ou équivalent) — jamais dans la catégorie par défaut utilisée par le lot 7.
2. **Récupérer son id** : `curl -H "X-Auth-Token: <jeton>" "http://<ip-tailscale-de-mcm8>:5100/v1/categories"` → reporter l'`id` correspondant dans `Watch__MinifluxCategoryId` (`.env`).
3. **Définir les sujets suivis** : `Watch__Topics__0__Name`/`Watch__Topics__0__Description`, etc.
4. **Seeder le curseur d'entrée** (même logique qu'à l'étape 12.5, sur l'endpoint de la catégorie cette fois) :

   ```bash
   curl -H "X-Auth-Token: <jeton>" "http://<ip-tailscale-de-mcm8>:5100/v1/categories/<id>/entries?order=id&direction=desc&limit=1"
   ```

   ```sql
   INSERT INTO "PollingStates" ("SourceType", "LastSourceItemId", "LastCreatedUtc", "UpdatedAtUtc")
   VALUES ('Watch', '<id_recupere_ci_dessus>', NULL, '<date_ISO8601_UTC>');
   ```

5. **Activer** : `Worker__TopicWatchEnabled=true` dans `.env`, puis `sudo docker compose up -d loreai-worker`.
