# LoreAI

Outil .NET 10 qui trie automatiquement le backlog de la collection **« Non trié »** de Raindrop.io. Il apprend vos collections et tags réels (via l'API Raindrop), classifie chaque nouvel article avec Claude Haiku en s'appuyant sur cette taxonomie, puis applique directement le résultat : tags fusionnés et déplacement vers la collection identifiée si elle correspond — sans étape de validation manuelle. Une notification Discord signale en plus les articles jugés prioritaires à tester, un compte-rendu Discord clôture chaque cycle ayant traité au moins un article, et un rapport hebdomadaire (doublons, tags à nettoyer, collections déséquilibrées, tendances, coût LLM) est envoyé en pièce jointe Markdown.

Tout ce qui se trouve **en dehors** de « Non trié » est considéré comme déjà classé par vos soins et n'est jamais retouché.

Voir [docs/adr/](docs/adr/) pour le détail des décisions d'architecture, notamment [0007](docs/adr/0007-apprentissage-taxonomie-non-trie.md) pour la logique d'apprentissage de la taxonomie. Pour contribuer, voir [docs/versioning.md](docs/versioning.md) : le titre des Pull Requests suit SemVer via Conventional Commits et pilote la version publiée automatiquement.

## Architecture

```
src/LoreAI.Core            modèles, enums, interfaces (zéro dépendance externe)
src/LoreAI.Infrastructure  Raindrop API (raindrops + collections + tags), classification Anthropic, persistance PostgreSQL (EF Core), notifications
src/LoreAI.Worker          Worker Service (Coravel pour la planification, Serilog pour les logs)
tests/                         xUnit.v3 + NSubstitute + WireMock.Net
docs/adr/                      Architecture Decision Records
```

## Roadmap

L'outil classe et range, mais n'exploite pas encore ce qu'il a accumulé. [docs/roadmap.md](docs/roadmap.md) cartographie les scénarios envisagés pour synthétiser, nettoyer et relire ce contenu (serveur MCP, revues thématiques, détection de doublons, file de lecture), priorisés valeur/effort et découpés en lots livrables.

## Prérequis

- .NET 10 SDK (dev local)
- Docker + Docker Compose (déploiement, notamment sur Raspberry Pi 64-bit / arm64 ; requis aussi en dev pour `dotnet test`, cf. section Tests)
- Un accès réseau à l'instance PostgreSQL mutualisée du Pi (ADR 0009) — base `loreai` et rôle applicatif provisionnés au préalable, voir [docs/deploiement-raspberry-pi.md](docs/deploiement-raspberry-pi.md)
- Un token API Raindrop.io (App Management Console → Test token)
- Une clé API Anthropic
- Un webhook Discord

## Configuration

Copier `.env.example` en `.env` et renseigner les valeurs (voir commentaires dans le fichier). Les clés suivent la convention .NET `Section__Propriete` (ex. `Raindrop__Token`, `Discord__WebhookUrl`).

⚠️ Vérifiez que `Raindrop__CollectionId` vaut bien **`-1`** (« Non trié »). C'est ce réglage qui garantit que l'outil ne touche pas aux raindrops que vous avez déjà rangés : `0` viserait **toute votre bibliothèque**.

## Lancer en local

```bash
dotnet build LoreAI.slnx
dotnet test LoreAI.slnx
dotnet run --project src/LoreAI.Worker
```

En local, `appsettings.Development.json` pointe vers l'instance PostgreSQL mutualisée du Pi (à joindre via son nom MagicDNS Tailscale, ADR 0010) et des logs dans `logs/`.

Le worker **refuse de démarrer** si la configuration est incomplète (token Raindrop, clé Anthropic, webhook Discord) et indique précisément le champ fautif — par exemple `DataAnnotation validation failed for 'RaindropApiOptions' members: 'Token'`. Renseignez les valeurs via `dotnet user-secrets` ou `.env` avant de lancer.

## Déploiement sur Raspberry Pi

Rien n'est compilé sur le Pi : l'image est construite et publiée par la CD GitHub sur `ghcr.io`, en multi-arch (`linux/amd64` + `linux/arm64`).

Pour un déploiement de zéro sur un Raspberry Pi fraîchement installé (installation de Docker incluse, récupération des secrets, dépannage), voir [docs/deploiement-raspberry-pi.md](docs/deploiement-raspberry-pi.md). Version courte ci-dessous pour une mise à jour ou un Pi déjà équipé de Docker :

```bash
uname -m                                        # doit renvoyer aarch64 (Raspberry Pi OS 64-bit)
mkdir -p data && sudo chown -R 1654:1654 data   # le conteneur tourne en non-root (uid 1654)
docker compose pull
docker compose up -d
docker compose logs -f
```

Pour épingler une version plutôt que de suivre `latest` : `LOREAI_TAG=0.3.0 docker compose up -d` (ou la variable dans le `.env`).

Le conteneur s'exécute sous l'utilisateur applicatif non-root de l'image .NET (`uid 1654`), à partir d'une image « chiselée » sans shell ni gestionnaire de paquets. Sur un bind mount, c'est la propriété côté hôte qui prime pour le dossier de logs : sans le `chown` ci-dessus, l'écriture échoue avec un `Permission denied` sur `/data`. Si vous mettez à jour une installation existante qui tournait en root, appliquez le `chown` sur le dossier `data/` déjà présent.

Pour reconstruire l'image localement malgré tout (mise au point) :

```bash
docker build -f src/LoreAI.Worker/Dockerfile -t loreai-worker:local .
```

Seuls les logs (`/data/logs/`) sont persistés via le volume `./data` : la base de données vit sur l'instance PostgreSQL mutualisée du Pi (ADR 0009), pas dans ce volume.

## ⚠️ Premier lancement

Sans état de polling préexistant, l'outil remonte **tout l'historique** de la collection « Non trié » au premier cycle (aucun webhook natif disponible côté API, cf. [ADR 0003](docs/adr/0003-strategie-polling-raindrop.md)). Si elle contient déjà beaucoup d'articles, cela peut être long et générer un volume d'appels LLM important, **et modifier automatiquement un grand nombre de raindrops d'un coup** (tags + déplacements). Pour éviter un traitement massif au premier lancement, insérez manuellement une ligne dans `PollingStates` avant de démarrer :

```sql
INSERT INTO "PollingStates" ("SourceType", "LastSourceItemId", "LastCreatedUtc", "UpdatedAtUtc")
VALUES ('Raindrop', '<id_du_dernier_raindrop_a_ignorer>', '<date_ISO8601_UTC>', '<date_ISO8601_UTC>');
```

## Comment le tri est appliqué

À chaque cycle (`Worker__PollingCronExpression`, toutes les 15 min par défaut) :
1. Apprentissage de la taxonomie réelle : collections (`GET /collections` + `/collections/childrens`) et tags (`GET /tags`) existants.
2. Pour chaque nouvel article de « Non trié », le LLM propose une collection existante (ou aucune), des tags, une action (à lire / à tester / référence) et une priorité.
3. Les tags proposés sont **toujours** appliqués (fusionnés avec les tags déjà présents, jamais de perte). Le raindrop n'est **déplacé** que si la collection proposée correspond exactement à une collection existante ; sinon il reste dans « Non trié » avec juste les tags. La note existante est complétée, jamais écrasée.

`Worker__WriteBackToRaindrop=false` désactive cette application automatique (mode classification + rapport seulement, rien n'est modifié dans Raindrop) — utile pour observer le comportement avant de laisser l'outil toucher à vos données. Actif (`true`) par défaut.

## Indexation de la bibliothèque (lot 1)

En parallèle, `LibraryIndexingJob` (`Worker__LibraryIndexCronExpression`, chaque dimanche 3h UTC par défaut) parcourt en **lecture seule** toute la bibliothèque Raindrop (`GET /raindrops/0`, hors corbeille) et la persiste dans `LibraryItems` — sans jamais classifier ni écrire quoi que ce soit dans Raindrop. `Worker__IndexLibraryOnStartup=true` déclenche en plus une passe au démarrage du worker, soumise à une garde : si la dernière passe complète date de moins de 24h, elle est ignorée (évite de solliciter l'API à chaque redémarrage rapproché).

## Connecteur newsletters Gmail (lot 8)

Désactivé par défaut (`Worker__EmailIngestionEnabled=false`) : ingère les mails portant un label Gmail donné, en extrait les vrais liens d'articles (filtre heuristique gratuit puis LLM), les classe comme les articles Raindrop — sans jamais rien réécrire dans Gmail ni Raindrop (ADR 0012).

Prérequis, à faire une fois avant d'activer (`Worker__EmailIngestionEnabled=true`) :

1. **Un label Gmail** qui identifie les mails à ingérer (ex. `Newsletters`) — créé et appliqué par un filtre Gmail de votre choix (expéditeur, domaine, présence d'un en-tête `List-Unsubscribe`...). LoreAI ne trie jamais le inbox lui-même, il fait confiance à ce label. Reporter son nom exact dans `Gmail__Label`.
2. **Un client OAuth Google** : [console.cloud.google.com](https://console.cloud.google.com) → créer un projet → activer l'API Gmail → écran de consentement en type **Externe** (« Interne » ne fonctionne que pour un compte Google Workspace, pas un Gmail personnel) avec votre compte ajouté aux **Test users** → créer des identifiants OAuth 2.0 de type **« Application Web »**, pas « Application de bureau » (un client Desktop n'a pas de redirect URI configurable, donc il est incompatible avec le Playground utilisé à l'étape suivante — `redirect_uri_mismatch` sinon) → dans ce client, ajouter `https://developers.google.com/oauthplayground` comme URI de redirection autorisée → reporter `ClientId`/`ClientSecret` dans `Gmail__ClientId`/`Gmail__ClientSecret`.
3. **Un refresh token**, obtenu une seule fois par consentement interactif — le worker ne fait jamais de flux OAuth interactif lui-même :
   - Ouvrir [OAuth 2.0 Playground](https://developers.google.com/oauthplayground), icône ⚙️ → cocher « Use your own OAuth credentials » et renseigner le ClientId/ClientSecret de l'étape 2 **avant** de faire l'étape 1 ci-dessous (sinon le token est émis sous le client de test partagé du Playground, pas le vôtre, et le refresh échouera plus tard avec `unauthorized_client`).
   - Étape 1 : sélectionner le scope `https://www.googleapis.com/auth/gmail.readonly` → « Authorize APIs » → se connecter avec le compte Gmail à surveiller (celui ajouté aux Test users).
   - Étape 2 : « Exchange authorization code for tokens » → copier le `refresh_token` obtenu dans `Gmail__RefreshToken`.
4. **Seeder le curseur `historyId`**, même logique que le « Premier lancement » de Raindrop ci-dessus (jamais de backfill automatique) : depuis la même page du Playground, utiliser l'`access_token` de l'étape 2 pour appeler `GET https://gmail.googleapis.com/gmail/v1/users/me/profile` (onglet « Use this token » du Playground, ou `curl -H "Authorization: Bearer <access_token>" https://gmail.googleapis.com/gmail/v1/users/me/profile`), puis insérer le `historyId` retourné :

```sql
INSERT INTO "PollingStates" ("SourceType", "LastSourceItemId", "LastCreatedUtc", "UpdatedAtUtc")
VALUES ('Newsletter', '<historyId_retourne_par_users.getProfile>', NULL, '<date_ISO8601_UTC>');
```

Sans cette ligne, `GmailIngester` refuse tout backfill et journalise un avertissement à chaque passage tant que le curseur n'est pas seedé.

## Connecteur RSS via Miniflux (lot 7)

Désactivé par défaut (`Worker__FeedIngestionEnabled=false`) : lit les nouvelles entrées d'une instance [Miniflux](https://miniflux.app/) auto-hébergée (tous flux confondus) et les classe comme les articles Raindrop — sans jamais rien réécrire dans Miniflux ni Raindrop (ADR 0012). Miniflux gère lui-même la liste d'abonnements, le parsing des flux et sert d'interface de lecture humaine (remplace Feedly) ; LoreAI ne fait que consommer ses entrées via son API REST.

Prérequis, à faire une fois avant d'activer (`Worker__FeedIngestionEnabled=true`) — voir `docs/deploiement-raspberry-pi.md` pour le déploiement complet du conteneur Miniflux :

1. **Déployer Miniflux** (service `miniflux` du `docker-compose.yml`) — sur sa propre base `miniflux` de l'instance PostgreSQL mutualisée du Pi (ADR 0009), jamais la base `loreai`.
2. **Créer le compte admin** au premier démarrage (`ADMIN_USERNAME`/`ADMIN_PASSWORD`), s'y connecter, puis **ajouter les flux RSS souhaités** via son interface.
3. **Générer un jeton API** : Settings → API Keys → Create a new API key → reporter dans `Miniflux__ApiToken`. Reporter aussi l'adresse interne du conteneur dans `Miniflux__BaseUrl` (ex. `http://miniflux:8080`, DNS du réseau Docker partagé).
4. **Seeder le curseur d'entrée**, même logique que Gmail/Raindrop ci-dessus (jamais de backfill automatique) : récupérer l'id de la dernière entrée existante avec `curl -H "X-Auth-Token: <jeton>" "http://<miniflux>/v1/entries?order=id&direction=desc&limit=1"`, puis :

```sql
INSERT INTO "PollingStates" ("SourceType", "LastSourceItemId", "LastCreatedUtc", "UpdatedAtUtc")
VALUES ('Feed', '<id_de_la_derniere_entree_a_ignorer>', NULL, '<date_ISO8601_UTC>');
```

Sans cette ligne, `MinifluxIngester` refuse tout backfill et journalise un avertissement à chaque passage tant que le curseur n'est pas seedé.

## Tests automatisés

```bash
dotnet test LoreAI.slnx
```

Aucune vraie clé nécessaire : les appels Raindrop/Anthropic/Discord sont simulés via WireMock.Net, la persistance utilise un conteneur PostgreSQL jetable par lot de tests (`Testcontainers.PostgreSql`) — **Docker est requis** pour lancer `dotnet test` (cf. ADR 0009).

## Vérification manuelle bout-en-bout (avec de vraies clés)

À faire avant un déploiement réel, pour observer le comportement sur votre compte Raindrop :

1. Renseigner dans `src/LoreAI.Worker/appsettings.Development.json` (ou via `dotnet user-secrets`, jamais commité) : `Raindrop__Token`, `Classifier__ApiKey`, `Discord__WebhookUrl`.
2. Pour ne pas attendre 15 min pendant les tests, surcharger temporairement l'expression cron :
   ```json
   "Worker": {
     "PollingCronExpression": "* * * * *",
     "WriteBackToRaindrop": false
   }
   ```
   Garder `WriteBackToRaindrop` à `false` le temps d'observer les suggestions dans les logs avant de laisser l'outil modifier vos raindrops.
3. Lancer :
   ```bash
   dotnet run --project src/LoreAI.Worker
   ```
4. Observer les logs (console + `logs/loreai-*.log`) : nombre de nouveaux articles détectés dans « Non trié », nombre de collections/tags appris, notifications envoyées.
5. Ajouter un raindrop test dans « Non trié » via l'app Raindrop pendant que le worker tourne, attendre le prochain cycle.
6. Inspecter la table `Articles` sur l'instance PostgreSQL (`psql` ou un client graphique) — vérifier les colonnes `SuggestedCollection`/`SuggestedTags`/`RecommendedAction`/`Priority`/`Reason`.
7. Repasser `WriteBackToRaindrop` à `true` pour valider l'application réelle (tags + déplacement) sur un raindrop de test, puis vérifier dans l'app Raindrop que le résultat correspond à la ligne en base.
8. Vérifier la réception Discord : l'alerte immédiate (si le raindrop test matche le seuil ATester/Haute) et le compte-rendu de fin de cycle (traités/déplacés/tags).

⚠️ Ce test réel consomme de vrais appels à l'API Anthropic (coût minime mais réel) et modifie votre vrai compte Raindrop dès que `WriteBackToRaindrop=true`.
