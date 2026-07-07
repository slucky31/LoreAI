# 0003 — Stratégie de polling Raindrop.io

## Statut
Acceptée

## Contexte
L'outil doit détecter les nouveaux articles ajoutés dans Raindrop.io. La documentation officielle de l'API REST (developer.raindrop.io) a été vérifiée : aucun mécanisme de webhook natif n'est proposé (même sur le plan payant), seules des intégrations tierces (Zapier, Make, IFTTT) existent hors API.

## Décision
Polling périodique via `GET /rest/v1/raindrops/{collectionId}?sort=-created&perpage=...&page=...`, avec un « high-water mark » (dernier `_id`/`created` connu) stocké en SQLite (table `PollingState`). Le client pagine tant que les pages sont pleines et qu'aucun item connu n'est rencontré ; il s'arrête dès qu'un item déjà traité apparaît ou qu'une page renvoie moins d'éléments que `perpage`.

## Conséquences
- Approche idempotente : un item peut être revu sans effet de bord grâce à l'upsert sur `Articles.Id`.
- Respecte la limite de 120 requêtes/minute de l'API Raindrop.
- **Premier lancement** : sans `PollingState` existant, tout l'historique Raindrop est remonté (backfill complet), ce qui peut être long et coûteux en appels LLM si le compte contient déjà des milliers d'articles. À anticiper avant la mise en production (cf. README).
