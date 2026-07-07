# RaindropAI

Outil .NET 9 qui surveille une collection Raindrop.io, classifie automatiquement les nouveaux articles via Claude Haiku (catégorie, action recommandée « à lire » / « à tester », priorité) et notifie sur Discord (alertes ciblées) et par email (digest quotidien).

Voir [docs/adr/](docs/adr/) pour le détail des décisions d'architecture.

## Architecture

```
src/RaindropAI.Core            modèles, enums, interfaces (zéro dépendance externe)
src/RaindropAI.Infrastructure  Raindrop API, classification Anthropic, persistance SQLite, notifications
src/RaindropAI.Worker          Worker Service (Coravel pour la planification, Serilog pour les logs)
tests/                         xUnit.v3 + NSubstitute + RichardSzalay.MockHttp
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
dotnet build RaindropAI.sln
dotnet test RaindropAI.sln
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

Sans état de polling préexistant, l'outil remonte **tout l'historique** de la collection Raindrop au premier cycle (aucun webhook natif disponible côté API, cf. [ADR 0003](docs/adr/0003-strategie-polling-raindrop.md)). Si votre collection contient déjà des milliers d'articles, cela peut être long et générer un volume d'appels LLM important. Pour éviter un backfill complet, insérez manuellement une ligne dans `PollingState` avant le premier lancement :

```sql
INSERT INTO PollingState (Id, LastRaindropId, LastCreatedUtc, UpdatedAtUtc)
VALUES (1, <id_du_dernier_raindrop_a_ignorer>, '<date_ISO8601_UTC>', '<date_ISO8601_UTC>');
```

## Écriture en retour dans Raindrop (optionnel)

`Worker__WriteBackToRaindrop=true` fait écrire le résultat de classification (tag + note) directement sur chaque raindrop via l'API. Désactivé par défaut.

## Tests

```bash
dotnet test RaindropAI.sln
```
