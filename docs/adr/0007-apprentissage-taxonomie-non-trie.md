# 0007 — Apprentissage de la taxonomie réelle et recentrage sur « Non trié »

## Statut
Acceptée — remplace partiellement l'ADR 0004 (la mécanique de classification LLM reste, la taxonomie change).

## Contexte
L'utilisateur a déjà des collections et des tags en place dans Raindrop.io. La taxonomie fixe initiale (`Category` : DotNet/ClaudeIA/Formation/Autre) ne reflétait pas cette organisation réelle. Le besoin exprimé : que l'outil apprenne les vraies collections/tags et trie spécifiquement le backlog de la collection spéciale « Non trié » (id `-1`), le reste de la bibliothèque étant considéré comme déjà classé et ne devant jamais être retouché.

Une mécanique de validation humaine avant application (réactions sur un message Discord, nécessitant un vrai bot avec connexion Gateway) a été envisagée puis explicitement écartée par l'utilisateur : trop complexe pour la valeur apportée.

## Décision
- **Taxonomie apprise dynamiquement** : à chaque cycle, `IRaindropClient.GetTaxonomyAsync` interroge `GET /collections`, `GET /collections/childrens` et `GET /tags` pour obtenir la liste réelle des collections et des tags (avec leur fréquence d'usage). Le `Category` enum fixe est supprimé.
- **Pipeline recentré exclusivement sur « Non trié »** (`RaindropApiOptions.CollectionId = -1` par défaut) : `UnsortedClassificationJob` (ex-`RaindropPollingJob`) ne traite plus « toute la collection », seulement les nouveaux items de Non trié.
- **Schéma LLM dynamique** : `ClassificationPromptBuilder.BuildToolInputSchemaJson(taxonomy)` construit l'`input_schema` de l'outil Anthropic à partir des titres de collections réels (champ `suggestedCollection`, enum incluant `null`). Les tags restent un tableau libre (non contraint par enum), avec les tags les plus utilisés listés dans le prompt pour inciter à leur réutilisation.
- **Application entièrement automatique, sans validation humaine** :
  - Les tags suggérés sont **toujours** appliqués, fusionnés avec les tags existants (jamais de perte, opération additive à faible risque).
  - Le raindrop n'est **déplacé** vers une collection que si `suggestedCollection` correspond exactement au titre d'une collection existante (vérifié côté code, pas seulement fait confiance au LLM) — sinon il reste dans Non trié avec seulement les tags appliqués.
  - La note existante est **complétée**, jamais écrasée (le nouveau texte de classification est ajouté à la suite de la note personnelle éventuelle).
- **`Worker__WriteBackToRaindrop` conservé comme interrupteur global** (actif par défaut) : seul garde-fou restant en l'absence de validation par item — permet de repasser en mode « classification + rapport seulement » sans toucher à Raindrop si besoin.

## Conséquences
- Le prompt et le schéma JSON envoyés au LLM varient d'un appel à l'autre selon la taxonomie du moment — plus de constante statique, coût de sérialisation dynamique mineur.
- Aucune perte de données possible par construction : tags additifs, note additive, déplacement conditionné à une correspondance exacte et vérifiée.
- Persiste le risque déjà documenté en ADR 0003 : le tout premier cycle traite l'intégralité du backlog historique de Non trié (peut être long/coûteux si le backlog est important).
- Sans validation humaine, une mauvaise suggestion de tag n'est pas bloquante (additif, faible risque) ; une mauvaise correspondance de collection est impossible par construction (soit ça correspond exactement à une collection existante, soit l'item reste dans Non trié).
