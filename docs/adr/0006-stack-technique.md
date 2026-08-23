# 0006 — Stack technique (librairies)

## Statut
Acceptée — le choix Dapper (premier point de la section Décision) est remplacé par l'[ADR 0011](0011-ef-core-remplace-dapper.md) ; MailKit est retiré par l'[ADR 0013](0013-retrait-canal-email.md) ; les autres décisions ci-dessous (Coravel, Serilog, résilience HTTP, tests, versions centralisées, warnings-as-errors) restent valables.

## Contexte
Plusieurs choix de librairies mineures ne changent pas la structure de la solution ni les interfaces de `Core`, seulement les classes concrètes d'`Infrastructure`/`Worker`. Ils sont regroupés dans un seul ADR pour éviter la fragmentation.

## Décision
- **Accès SQLite** : Dapper + `Microsoft.Data.Sqlite` — SQL explicite, léger, adapté à un schéma de deux tables, plutôt qu'EF Core (migrations automatiques jugées superflues ici).
- **Scheduling** : Coravel (`Schedule<T>().Cron(...)`) pour le cycle de polling et le digest quotidien, plutôt qu'un `PeriodicTimer` natif — expressions cron plus lisibles et configurables sans redéploiement.
- **Logging** : Serilog (console + fichier sur `/data/logs`), pour consulter l'historique sans dépendre uniquement de `docker logs`.
- **Résilience HTTP** : `Microsoft.Extensions.Http.Resilience` (Polly officiel .NET) sur les `HttpClient` typés (Raindrop, Anthropic, Discord) pour gérer retries/429 de façon standardisée.
- **Email** : MailKit, bibliothèque SMTP de référence en .NET.
- **Tests** : xUnit.v3 (Microsoft Testing Platform) + NSubstitute pour les doublures, WireMock.Net pour simuler les API HTTP externes via un vrai serveur HTTP local (préféré à RichardSzalay.MockHttp, sans mise à jour depuis 2023 : WireMock.Net est activement maintenu et exerce la vraie pile réseau, au prix d'une exécution un peu plus lente).
- **Versions NuGet centralisées** : `Directory.Packages.props` (`ManagePackageVersionsCentrally`) — une seule source de vérité pour les versions, les `.csproj` ne référencent que le nom du package.
- **Avertissements traités comme des erreurs** : `Directory.Build.props` (`TreatWarningsAsErrors`), appliqué à tous les projets pour éviter l'accumulation de warnings ignorés.

## Conséquences
- Toutes ces dépendances sont isolées dans `Infrastructure`/`Worker` ; `Core` reste sans dépendance externe.
- Remplacer l'une de ces librairies (ex. Coravel par Quartz.NET) est un changement localisé, sans impact sur `Core`.
- Faire monter la version d'un package se fait désormais à un seul endroit (`Directory.Packages.props`).
