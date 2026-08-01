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

## 3. Récupérer les secrets

| Secret | Où le récupérer |
|---|---|
| `Raindrop__Token` | Raindrop.io → **Settings → Integrations → App Management Console → For Developers → Create new app** → dans l'app créée, onglet **Test token** (ne pas passer par OAuth, ce token n'expire pas) |
| `Classifier__ApiKey` | [console.anthropic.com](https://console.anthropic.com) → **API Keys → Create Key** |
| `Discord__WebhookUrl` | Discord → paramètres du salon voulu → **Intégrations → Webhooks → Nouveau webhook** → copier l'URL |
| `Email__Smtp*` | Voir la section [Configuration SMTP Gmail](#configuration-smtp-gmail-optionnel) ci-dessous si vous utilisez Gmail, sinon vos identifiants SMTP habituels |

### Configuration SMTP Gmail (optionnel)

Gmail exige un mot de passe d'application (le mot de passe du compte ne fonctionne pas pour SMTP), ce qui nécessite la validation en deux étapes.

1. Activer la 2FA : [myaccount.google.com/security](https://myaccount.google.com/security) → **Validation en deux étapes**.
2. Créer le mot de passe d'application : [myaccount.google.com/apppasswords](https://myaccount.google.com/apppasswords) → nommer (ex. `RaindropAI`) → **Créer**. Copier le mot de passe de 16 caractères **sans les espaces** (non réaffiché ensuite).
3. Renseigner dans `.env` :
   ```bash
   Email__SmtpHost=smtp.gmail.com
   Email__SmtpPort=587
   Email__SmtpUser=votreadresse@gmail.com
   Email__SmtpPassword=<mot de passe d'application, 16 caractères>
   Email__FromAddress=votreadresse@gmail.com
   Email__ToAddress=votreadresse@gmail.com
   Email__Security=Auto
   ```
   `SmtpUser` et `FromAddress` doivent correspondre à l'adresse Gmail elle-même (Gmail rejette un `From` usurpé). `ToAddress` peut être une autre adresse.

Si le lien du mot de passe d'application ne s'affiche pas : la 2FA n'est probablement pas encore active, ou c'est un compte Google Workspace dont l'admin a désactivé les mots de passe d'application.

Une erreur `535-5.7.8 Username and Password not accepted` dans les logs indique presque toujours un mot de passe d'application mal copié ou la 2FA non activée.

## 4. Récupérer les fichiers de déploiement

Pas besoin de cloner le repo entier : seuls `docker-compose.yml` et `.env` sont nécessaires.

```bash
mkdir -p ~/raindropai && cd ~/raindropai
curl -O https://raw.githubusercontent.com/slucky31/RaindropAI/main/docker-compose.yml
curl -O https://raw.githubusercontent.com/slucky31/RaindropAI/main/.env.example
cp .env.example .env
```

## 5. Configurer le `.env`

```bash
nano .env
```

Renseigner les secrets récupérés à l'étape 3. Points d'attention :

- `Raindrop__CollectionId` doit rester à `-1` (« Non trié ») — `0` viserait toute la bibliothèque, y compris ce qui est déjà rangé.
- Laisser `Worker__WriteBackToRaindrop=false` au premier lancement, pour observer les suggestions dans les logs sans rien modifier dans Raindrop. Repasser à `true` une fois rassuré.
- `RAINDROPAI_TAG` (optionnel, dans `.env`) permet d'épingler une version précise (ex. `0.3.5`) plutôt que de suivre `latest`.

## 6. Préparer le dossier de données

```bash
mkdir -p data
sudo chown -R 1654:1654 data
```

Le conteneur tourne en non-root (uid 1654, image « chiselée » sans shell). Sur un bind mount, c'est la propriété côté hôte qui prime : sans ce `chown`, SQLite échoue à créer sa base avec un `Permission denied` sur `/data`.

## 7. Premier démarrage et limitation du backfill

Sans état de polling préexistant, l'outil remonte **tout l'historique** de « Non trié » dès le premier cycle (pas de webhook natif côté API, cf. [ADR 0003](adr/0003-strategie-polling-raindrop.md)). Si cette collection contient déjà beaucoup d'articles, cela peut déclencher un gros volume d'appels Anthropic et, si `WriteBackToRaindrop=true`, modifier énormément de raindrops d'un coup. Le cycle de polling tourne toutes les 15 min par défaut (`Worker__PollingCronExpression`) : il y a donc une fenêtre pour agir après le tout premier démarrage.

```bash
sudo docker compose pull
sudo docker compose up -d
```

Le premier démarrage crée le fichier SQLite (`/data/raindropai.db`, schéma appliqué immédiatement) avant même le premier cycle de polling. Pour repartir d'un point précis plutôt que de tout l'historique, arrêtez le conteneur tout de suite et seedez `PollingState` :

```bash
sudo docker compose stop
sudo apt install -y sqlite3
sqlite3 data/raindropai.db
```

```sql
INSERT INTO PollingState (Id, LastRaindropId, LastCreatedUtc, UpdatedAtUtc)
VALUES (1, <id_du_dernier_raindrop_a_ignorer>, '<date_ISO8601_UTC>', '<date_ISO8601_UTC>');
```

Puis relancer :

```bash
sudo docker compose up -d
sudo docker compose logs -f
```

Le fichier SQLite (`/data/raindropai.db`) et les logs (`/data/logs/`) sont persistés via le volume `./data`.

## 8. Mettre à jour une installation existante

```bash
cd ~/raindropai
sudo docker compose pull
sudo docker compose up -d
```

## 9. Dépannage

**`You must install or update .NET to run this application` dans les logs**
L'image publiée ciblait une version de framework .NET différente de celle embarquée dans le runtime du conteneur (bug corrigé en v0.3.5 — voir [PR #26](https://github.com/slucky31/RaindropAI/pull/26)). Repasser par l'étape 8 pour récupérer une image à jour.

**`Permission denied` sur `/data` au démarrage**
Le dossier `data/` côté hôte n'appartient pas à l'uid 1654. Refaire l'étape 6 (`sudo chown -R 1654:1654 data`) — nécessaire aussi après mise à jour d'une installation qui tournait auparavant en root.

**Aucune notification Discord / email reçue**
Vérifier `Discord__WebhookUrl` et les `Email__Smtp*` dans `.env`, puis `sudo docker compose logs -f` pour l'erreur d'envoi exacte.
