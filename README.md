# RaindropAI

Outil .NET 9 qui trie automatiquement le backlog de la collection **« Non trié »** de Raindrop.io. Il apprend vos collections et tags réels (via l'API Raindrop), classifie chaque nouvel article avec Claude Haiku en s'appuyant sur cette taxonomie, puis applique directement le résultat : tags fusionnés et déplacement vers la collection identifiée si elle correspond — sans étape de validation manuelle. Une notification Discord signale en plus les articles jugés prioritaires à tester, et un digest email quotidien récapitule tout le reste.

Tout ce qui se trouve **en dehors** de « Non trié » est considéré comme déjà classé par vos soins et n'est jamais retouché.

Voir [docs/adr/](docs/adr/) pour le détail des décisions d'architecture, notamment [0007](docs/adr/0007-apprentissage-taxonomie-non-trie.md) pour la logique d'apprentissage de la taxonomie. Pour contribuer, voir [docs/versioning.md](docs/versioning.md) : le titre des Pull Requests suit SemVer via Conventional Commits et pilote la version publiée automatiquement.

## Architecture

```
src/RaindropAI.Core            modèles, enums, interfaces (zéro dépendance externe)
src/RaindropAI.Infrastructure  Raindrop API (raindrops + collections + tags), classification Anthropic, persistance SQLite, notifications
src/RaindropAI.Worker          Worker Service (Coravel pour la planification, Serilog pour les logs)
tests/                         xUnit.v3 + NSubstitute + WireMock.Net
docs/adr/                      Architecture Decision Records
```

## Prérequis

- .NET 9 SDK (dev local)
- Docker + Docker Compose (déploiement, notamment sur Raspberry Pi 64-bit / arm64)
- Un token API Raindrop.io (App Management Console → Test token)
- Une clé API Anthropic
- Un webhook Discord
- Un compte SMTP pour l'envoi du digest

## Configuration

Copier `.env.example` en `.env` et renseigner les valeurs (voir commentaires dans le fichier). Les clés suivent la convention .NET `Section__Propriete` (ex. `Raindrop__Token`, `Email__SmtpHost`).

## Lancer en local

```bash
dotnet build RaindropAI.slnx
dotnet test RaindropAI.slnx
dotnet run --project src/RaindropAI.Worker
```

En local, `appsettings.Development.json` pointe vers un fichier SQLite `raindropai.dev.db` dans le dossier courant et des logs dans `logs/`.

## Déploiement sur Raspberry Pi

```bash
uname -m            # doit renvoyer aarch64 (Raspberry Pi OS 64-bit)
docker compose build
docker compose up -d
docker compose logs -f
```

Le build est fait directement sur le Pi : les images `mcr.microsoft.com/dotnet/*` sont multi-arch, la variante arm64 est récupérée automatiquement.

Le fichier SQLite (`/data/raindropai.db`) et les logs (`/data/logs/`) sont persistés via le volume `./data`.

## ⚠️ Premier lancement

Sans état de polling préexistant, l'outil remonte **tout l'historique** de la collection « Non trié » au premier cycle (aucun webhook natif disponible côté API, cf. [ADR 0003](docs/adr/0003-strategie-polling-raindrop.md)). Si elle contient déjà beaucoup d'articles, cela peut être long et générer un volume d'appels LLM important, **et modifier automatiquement un grand nombre de raindrops d'un coup** (tags + déplacements). Pour éviter un traitement massif au premier lancement, insérez manuellement une ligne dans `PollingState` avant de démarrer :

```sql
INSERT INTO PollingState (Id, LastRaindropId, LastCreatedUtc, UpdatedAtUtc)
VALUES (1, <id_du_dernier_raindrop_a_ignorer>, '<date_ISO8601_UTC>', '<date_ISO8601_UTC>');
```

## Comment le tri est appliqué

À chaque cycle (`Worker__PollingCronExpression`, toutes les 15 min par défaut) :
1. Apprentissage de la taxonomie réelle : collections (`GET /collections` + `/collections/childrens`) et tags (`GET /tags`) existants.
2. Pour chaque nouvel article de « Non trié », le LLM propose une collection existante (ou aucune), des tags, une action (à lire / à tester / référence) et une priorité.
3. Les tags proposés sont **toujours** appliqués (fusionnés avec les tags déjà présents, jamais de perte). Le raindrop n'est **déplacé** que si la collection proposée correspond exactement à une collection existante ; sinon il reste dans « Non trié » avec juste les tags. La note existante est complétée, jamais écrasée.

`Worker__WriteBackToRaindrop=false` désactive cette application automatique (mode classification + rapport seulement, rien n'est modifié dans Raindrop) — utile pour observer le comportement avant de laisser l'outil toucher à vos données. Actif (`true`) par défaut.

## Tests automatisés

```bash
dotnet test RaindropAI.slnx
```

Aucune vraie clé nécessaire : les appels Raindrop/Anthropic/Discord sont simulés via WireMock.Net, la base est un fichier SQLite temporaire par test.

## Vérification manuelle bout-en-bout (avec de vraies clés)

À faire avant un déploiement réel, pour observer le comportement sur votre compte Raindrop :

1. Renseigner dans `src/RaindropAI.Worker/appsettings.Development.json` (ou via `dotnet user-secrets`, jamais commité) : `Raindrop__Token`, `Classifier__ApiKey`, `Discord__WebhookUrl`, `Email__Smtp*`.
2. Pour ne pas attendre 15 min / 24h pendant les tests, surcharger temporairement les expressions cron :
   ```json
   "Worker": {
     "PollingCronExpression": "* * * * *",
     "DigestCronExpression": "*/2 * * * *",
     "WriteBackToRaindrop": false
   }
   ```
   Garder `WriteBackToRaindrop` à `false` le temps d'observer les suggestions dans les logs avant de laisser l'outil modifier vos raindrops.
3. Lancer :
   ```bash
   dotnet run --project src/RaindropAI.Worker
   ```
4. Observer les logs (console + `logs/raindropai-*.log`) : nombre de nouveaux articles détectés dans « Non trié », nombre de collections/tags appris, notifications envoyées.
5. Ajouter un raindrop test dans « Non trié » via l'app Raindrop pendant que le worker tourne, attendre le prochain cycle.
6. Inspecter `raindropai.dev.db` (créé à la racine du projet en dev) avec DB Browser for SQLite ou `sqlite3` — vérifier les colonnes `SuggestedCollection`/`SuggestedTags`/`RecommendedAction`/`Priority`/`Reason`.
7. Repasser `WriteBackToRaindrop` à `true` pour valider l'application réelle (tags + déplacement) sur un raindrop de test, puis vérifier dans l'app Raindrop que le résultat correspond à la ligne SQLite.
8. Vérifier la réception Discord (si le raindrop test matche le seuil ATester/Haute) et le digest email (regroupement par collection puis action).

⚠️ Ce test réel consomme de vrais appels à l'API Anthropic (coût minime mais réel) et modifie votre vrai compte Raindrop dès que `WriteBackToRaindrop=true`.
