# 0006 — Stack technique (librairies)

## Statut
Acceptée

## Contexte
Plusieurs choix de librairies mineures ne changent pas la structure de la solution ni les interfaces de `Core`, seulement les classes concrètes d'`Infrastructure`/`Worker`. Ils sont regroupés dans un seul ADR pour éviter la fragmentation.

## Décision
- **Accès SQLite** : Dapper + `Microsoft.Data.Sqlite` — SQL explicite, léger, adapté à un schéma de deux tables, plutôt qu'EF Core (migrations automatiques jugées superflues ici).
- **Scheduling** : Coravel (`Schedule<T>().Cron(...)`) pour le cycle de polling et le digest quotidien, plutôt qu'un `PeriodicTimer` natif — expressions cron plus lisibles et configurables sans redéploiement.
- **Logging** : Serilog (console + fichier sur `/data/logs`), pour consulter l'historique sans dépendre uniquement de `docker logs`.
- **Résilience HTTP** : `Microsoft.Extensions.Http.Resilience` (Polly officiel .NET) sur les `HttpClient` typés (Raindrop, Anthropic, Discord) pour gérer retries/429 de façon standardisée.
- **Email** : MailKit, bibliothèque SMTP de référence en .NET.
- **Tests** : xUnit.v3 (Microsoft Testing Platform) + NSubstitute pour les doublures, RichardSzalay.MockHttp pour simuler les API HTTP externes sans appel réseau réel.

## Conséquences
- Toutes ces dépendances sont isolées dans `Infrastructure`/`Worker` ; `Core` reste sans dépendance externe.
- Remplacer l'une de ces librairies (ex. Coravel par Quartz.NET) est un changement localisé, sans impact sur `Core`.
