# 0009 — PostgreSQL mutualisé auto-hébergé sur le Raspberry Pi

## Statut
Acceptée — remplace l'[ADR 0002](0002-persistance-sqlite-embarquee.md) (persistance SQLite embarquée).

## Contexte

L'[ADR 0002](0002-persistance-sqlite-embarquee.md) avait retenu SQLite en clarifiant la contrainte initiale « pas de base de données » : l'objectif était d'éviter **un serveur à administrer** (« PostgreSQL, MySQL... »), pas de proscrire le stockage structuré. Ce raisonnement était juste tant que LoreAI était seul sur la machine.

Deux choses ont changé.

**1. Une instance PostgreSQL va exister sur le Pi de toute façon.** D'autres projets personnels en ont besoin, et la [roadmap](../roadmap.md) elle-même en fait apparaître un troisième : Miniflux, envisagé comme interface de lecture RSS, **exige PostgreSQL**. La question n'est donc plus « faut-il ajouter un serveur de base de données pour LoreAI ? » mais « LoreAI doit-il rester sur SQLite alors qu'une instance mutualisée tournera à côté ? ». Le coût marginal invoqué par l'ADR 0002 — l'administration d'un serveur — est désormais **payé par ailleurs**, quoi qu'il arrive.

**2. Le moment est le moins coûteux possible.** La roadmap établit que la table `Articles` ne contient que ce qui est passé par « Non trié » depuis le premier démarrage : le corpus est quasi vide. Le runner de migrations du lot 0 n'est pas écrit, et le lot 1 n'a pas encore indexé des milliers d'items. Migrer plus tard signifierait migrer un corpus volumineux **et** réécrire un runner de migrations déjà livré.

Il faut être honnête sur ce qui **ne motive pas** cette décision : SQLite ne bloquait rien pour LoreAI seul. Il n'y a aujourd'hui ni problème de volume, ni problème de performance, ni contention d'écriture. Cette décision est un arbitrage de **mutualisation et d'outillage**, pas une correction de limite technique.

Matériel visé : Raspberry Pi 4, 8 Go de RAM, stockage sur SSD USB. Postgres y tourne confortablement, et l'usure d'écriture qui aurait posé question sur carte SD n'est pas un sujet.

## Décision

Migrer la persistance de LoreAI vers **PostgreSQL auto-hébergé sur le Raspberry Pi**, sur une **instance mutualisée** partagée avec les autres projets de la machine.

### L'instance n'appartient pas à LoreAI

C'est le point structurant, et il conditionne tout le reste.

- L'instance PostgreSQL est un **composant d'infrastructure de la machine**, déployé par sa propre stack Docker Compose, avec son propre volume et son propre cycle de vie. Elle ne fait **pas** partie du `docker-compose.yml` de LoreAI.
- LoreAI s'y raccorde via un **réseau Docker externe** partagé. Pas de `depends_on` vers un service que LoreAI ne possède pas : le worker doit démarrer, échouer proprement et réessayer si la base n'est pas encore disponible.
- LoreAI obtient une **base dédiée** (`loreai`) et un **rôle dédié**, propriétaire de son seul schéma. Pas de `SUPERUSER`, pas de droit sur les bases voisines, pas de réglage au niveau du cluster.
- Toute **extension** (`pg_trgm`, `vector`, `hstore`…) est installée par le propriétaire de l'instance, pas par le code applicatif au démarrage : `CREATE EXTENSION` requiert des droits que le rôle applicatif n'a pas, et ne doit pas avoir.
- La **version majeure est épinglée** et devient une décision transverse : une montée de version majeure impacte tous les locataires en même temps.

### Ce que LoreAI y gagne concrètement

- **Recherche plein texte réellement multilingue.** `to_tsvector('french', ...)` apporte un vrai dictionnaire et du stemming, là où FTS5 n'offre qu'un tokenizer `unicode61` sans racinisation. Le corpus étant majoritairement francophone, la différence est réelle. Cela supprime au passage la vérification de disponibilité de FTS5 inscrite au lot 0.
- **Vecteurs natifs, si besoin.** `pgvector` rend la question des embeddings (scénario S5) triviale au lieu d'imposer une dépendance supplémentaire — sans obliger à s'en servir.
- **Types véritables.** `timestamptz` au lieu de chaînes ISO 8601, `jsonb` indexable pour la réponse brute du modèle (le bloc `usage` du scénario S6 devient interrogeable sans `json_extract` sur du texte), `text[]` pour les tags au lieu d'un tableau JSON sérialisé.
- **Plusieurs écrivains.** La contrainte « un seul writer à la fois » de l'ADR 0002 disparaît. Elle n'est pas gênante aujourd'hui, mais la phase 2 de la roadmap prévoit trois ingéreurs de sources (Raindrop, RSS, Gmail) susceptibles d'écrire en parallèle.
- **Le MCP en lecture seule devient propre.** Un rôle `loreai_ro` avec `GRANT SELECT` remplace la combinaison `Mode=ReadOnly` + montage `:ro`, plus fragile parce qu'elle reposait sur deux garde-fous indépendants portant sur un fichier.

### Ce qui ne change pas

- ~~Dapper reste l'accès aux données~~ — **amendé par l'[ADR 0011](0011-ef-core-remplace-dapper.md)**, décidée après cet ADR : EF Core remplace Dapper intégralement (schéma et requêtes), pas seulement `Npgsql` à la place de `Microsoft.Data.Sqlite`.
- **Pas d'EF Core, pas de migrations automatiques.** L'ADR 0006 tient : SQL explicite, runner de migrations maison lisant des scripts numérotés.
- **`Core` reste sans dépendance externe.** `Npgsql` ne remonte pas au-delà d'`Infrastructure`.
- **Le rythme de sauvegarde reste une contrainte de la machine**, pas de LoreAI — mais LoreAI cesse d'être sauvegardable par simple copie de fichier (voir Conséquences).

## Alternatives écartées

- **Rester sur SQLite.** Reste défendable pour LoreAI pris isolément — c'était d'ailleurs la conclusion de l'arbitrage « Neon » de la roadmap. Écarté parce que l'argument central de l'ADR 0002 (« éviter un serveur à administrer ») cesse d'être vrai dès lors que le serveur existe pour d'autres projets : on paierait le coût d'administration **sans** en tirer aucun bénéfice pour LoreAI.
- **PostgreSQL hébergé (Neon et équivalents).** Déjà écarté dans la roadmap et **cette décision ne le réhabilite pas** : le plan gratuit plafonne à 0,5 Go — insuffisant dès que le contenu réel des articles sera stocké — et surtout une base distante rend le worker dépendant d'Internet **pour écrire son propre curseur de polling**. Aujourd'hui une coupure réseau interrompt les appels Raindrop et Anthropic, mais la base reste saine. C'est exactement ce que l'auto-hébergement préserve.
- **Une instance PostgreSQL dédiée à LoreAI.** Plus simple à raisonner, mais c'est un second serveur à maintenir sur la même machine pour ~aucun bénéfice, et cela contredit la raison même d'adopter Postgres ici.
- **SQLite pour LoreAI + PostgreSQL pour les autres projets.** Deux technologies de persistance à connaître, deux stratégies de sauvegarde, deux outillages — et LoreAI reste le seul à ne pas profiter de l'instance.
- **MySQL / MariaDB.** Aucun avantage ici, et perd `to_tsvector` avec dictionnaire français, `jsonb` et `pgvector`. Miniflux impose Postgres de toute façon.
- **Migrer plus tard, après le lot 1.** Explicitement rejeté : c'est le scénario le plus coûteux. Il impose de migrer un corpus volumineux et de réécrire un runner de migrations déjà livré pour SQLite.

## Conséquences

### Positives

- Une seule technologie de persistance sur la machine, un seul outillage (`psql`, `pg_dump`, pgAdmin), une seule stratégie de sauvegarde.
- Miniflux devient quasi gratuit à ajouter : son prérequis PostgreSQL est déjà satisfait. L'arbitrage « Miniflux » de la roadmap perd son principal argument de coût.
- La vérification de disponibilité de FTS5 prévue au lot 0 disparaît, remplacée par une capacité supérieure et garantie.
- `pgvector` lève la question ouverte des embeddings sans engagement immédiat.

### Négatives, et assumées

- **La suite de tests perd sa propriété « zéro dépendance ».** Aujourd'hui la persistance est testée sur un fichier SQLite temporaire par test, sans rien installer. Avec PostgreSQL il faut une vraie base joignable. C'est la contrepartie la plus concrète de cette décision, et elle touche le quotidien de développement.

  **Stratégie retenue : `Testcontainers.PostgreSql`, local et CI identiques** (revu par l'[ADR 0011](0011-ef-core-remplace-dapper.md) — la version d'origine de cet ADR prévoyait un service `postgres` natif distinct en CI ; `ubuntu-latest` a déjà Docker, ce qui rend Testcontainers suffisant partout sans mécanisme séparé). Chaque exécution obtient une base jetable et isolée, ce qui préserve la propriété qui comptait vraiment dans le montage SQLite — des tests indépendants et parallélisables — au prix d'un démarrage plus lent et d'un daemon Docker requis.

  **Contrainte connue, à ne pas re-découvrir :** le Shadow PC ne peut pas exécuter la suite de tests. Sa plateforme n'expose pas la virtualisation imbriquée (`wsl --install` échoue sur `HCS_E_HYPERV_NOT_INSTALLED`), donc ni WSL2, ni Hyper-V, ni Docker Desktop. Le développement se fait depuis un environnement disposant de Docker ; le Shadow reste utilisable pour tout le reste — lecture, rédaction, `git`, appels d'API. Si un jour il devait redevenir le poste principal, il faudrait replier sur un PostgreSQL natif Windows avec isolation par schéma, et non sur Testcontainers.
- **La sauvegarde n'est plus une copie de fichier.** `/data/loreai.db` disparaît au profit d'un `pg_dump` planifié. C'est précisément l'administration que l'ADR 0002 voulait éviter — mutualisée, mais réelle.
- **Une panne de l'instance arrête LoreAI**, alors qu'un fichier SQLite n'a pas d'état « indisponible ». Le worker doit traiter l'indisponibilité de la base comme une panne transitoire, journalisée, non fatale — au même titre que l'API Raindrop.
- **Les montées de version majeures deviennent transverses** : elles ne peuvent plus être décidées projet par projet.
- **Empreinte mémoire non nulle** (quelques centaines de Mo selon `shared_buffers`), à répartir entre tous les locataires. Non bloquant sur 8 Go, mais ce n'est plus zéro.
- **La contrainte de l'ADR 0002 « pas de scénario multi-instance » n'est plus imposée par la technique.** Elle reste vraie par choix : LoreAI demeure mono-utilisateur, et rien dans cette décision ne justifie de rouvrir ce point.

### Travaux induits (à planifier au lot 0 de la roadmap)

Ils doivent être faits **dans le même lot que le passage au modèle multi-sources** : les deux réécrivent le schéma, et les enchaîner reviendrait à migrer deux fois.

1. Déployer l'instance mutualisée (stack Compose dédiée, réseau externe, volume sur le SSD, version majeure épinglée, image compatible arm64).
2. Créer la base `loreai`, le rôle propriétaire et le rôle `loreai_ro` en lecture seule destiné au MCP.
3. ~~Remplacer `Microsoft.Data.Sqlite` par `Npgsql`~~ — remplacer Dapper par EF Core (`Npgsql.EntityFrameworkCore.PostgreSQL`) dans `Infrastructure` : voir [ADR 0011](0011-ef-core-remplace-dapper.md). `IDbContextFactory` fournit le pooling de connexions natif de Npgsql.
4. ~~Écrire le runner de migrations directement en PostgreSQL~~ — superflu : les migrations EF Core (fichiers C# générés) et leur table de suivi `__EFMigrationsHistory` en tiennent lieu (ADR 0011). Transposer `0001_InitialSchema.sql` (`TEXT` horodaté → `timestamptz`, tags JSON → `text[]`, réponse brute → `jsonb`, `INTEGER` booléen → `boolean`) reste vrai, obtenu par les types C# du modèle EF Core plutôt qu'écrit à la main.
5. Reprendre les données existantes. Volume attendu : faible. Un script de transfert ponctuel suffit — il n'a pas vocation à être conservé.
6. Adapter les tests de persistance (`Testcontainers.PostgreSql`, local et CI identiques — voir [ADR 0011](0011-ef-core-remplace-dapper.md), pas de service `postgres` séparé dans le workflow GitHub Actions).
7. Mettre à jour `README.md`, `.env.example` (`Sqlite__ConnectionString` → `Postgres__ConnectionString`), `docker-compose.yml`, le guide de déploiement Raspberry Pi et `CLAUDE.md`.
8. Mettre en place la sauvegarde `pg_dump` planifiée **avant** de supprimer le fichier SQLite.
