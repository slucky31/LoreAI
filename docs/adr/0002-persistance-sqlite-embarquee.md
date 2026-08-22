# 0002 — Persistance SQLite embarquée

## Statut
Remplacée par l'[ADR 0009](0009-postgresql-mutualise-sur-le-pi.md) — le raisonnement ci-dessous (éviter un **serveur** à administrer) reste juste, mais sa prémisse a changé : une instance PostgreSQL mutualisée existe désormais sur la machine pour d'autres projets, ce qui annule le coût marginal invoqué ici.

## Contexte
La contrainte initiale exprimée était « pas de base de données ». L'outil a néanmoins besoin de conserver deux choses entre les cycles d'exécution : l'état de polling (dernier raindrop connu) et le résultat de classification de chaque article, avec des requêtes de regroupement pour le digest (par catégorie/action, articles non encore envoyés).

## Décision
Utiliser SQLite embarqué (fichier unique, monté sur un volume Docker), plutôt que :
- des fichiers JSON plats : plus fragiles pour les requêtes de regroupement et la gestion de concurrence lecture/écriture ;
- l'absence totale de persistance : impossible de savoir quels articles ont déjà été traités/notifiés sans revenir au tri manuel.

Ceci clarifie la contrainte initiale : l'objectif était d'éviter un **serveur** de base de données à administrer (PostgreSQL, MySQL...), pas de proscrire toute forme de stockage structuré. SQLite ne nécessite aucun serveur, aucune administration, juste un fichier.

## Conséquences
- Accès via Dapper + Microsoft.Data.Sqlite (cf. ADR 0006).
- Le fichier `.db` doit être sur un volume Docker persistant (`/data`) pour survivre aux redémarrages du conteneur.
- Pas de scénario multi-instance : un seul writer à la fois, ce qui correspond à l'usage personnel visé.
