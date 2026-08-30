# Roadmap — Phase 3 : consolider et exploiter

> Écrite le **2026-08-30**, après revue complète du code, des 22 issues ouvertes et de la production.
> Suite de [`roadmap.md`](roadmap.md), qui couvre les phases 1 et 2 (lots 0 → 9, **tous livrés**) et reste la référence pour les décisions D1-D7 et les arbitrages déjà tranchés.
> Les constats qui motivent ce document sont dans [`critique-fonctionnelle.md`](critique-fonctionnelle.md).

## Pourquoi une phase 3

La phase 1 avait un objectif clair : *transformer une base en écriture seule en actif exploitable*. La phase 2 : *élargir les sources*. Les deux sont faites. Il reste un seul lot de l'ancienne carte (déduplication, [#51](https://github.com/slucky31/LoreAI/issues/51)), et le projet se retrouve sans cap.

Ce n'est pas un vide : la revue fonctionnelle a mis en évidence un déséquilibre net entre **ce qui a été construit** et **ce qui est réellement exploité**.

| | |
|---|---|
| Fonctionnalités livrées en 3 mois | 10 lots, 4 projets, ~14 700 lignes, 431 tests |
| Fonctionnalités actives par défaut | le cycle « Non trié », l'indexation, les deux rapports, la réconciliation |
| Fonctionnalités livrées mais désactivées | Gmail, RSS, file de lecture taguée, veille — **4 sur les 6 derniers lots** |
| Données collectées et jamais relues | surlignages, corrections humaines, `important`/`broken` (0 sur 1 369) |
| Sauvegardes | **aucune** |

La phase 3 ne cherche donc pas à ajouter des sources ni des jobs. Elle a trois objectifs, dans cet ordre :

1. **Reprendre en main l'exploitation** — savoir ce qui tourne, ne rien perdre, ne pas dériver en coût.
2. **Refermer les boucles ouvertes** — le signal humain capté puis jeté, les données stockées puis ignorées.
3. **Ouvrir l'usage quotidien** — le second cerveau, seule promesse de départ encore non tenue.

## Décisions actées (session du 2026-08-30)

Ces quatre points sont tranchés et contraignent le reste du document.

| # | Décision | Conséquence |
|---|---|---|
| **D8** | **Budget LLM : 10 €/mois, avec garde-fou dur dans le code.** La mesure a posteriori de S6 ne suffit pas — voir F2. | Introduit une table `LlmCalls` unifiée et une interface `ILlmBudgetGuard` consultée **avant** chaque appel LLM. Au franchissement du seuil : les jobs consommateurs de LLM s'arrêtent proprement, une alerte part une seule fois, le pipeline non-LLM continue. Lot 12 |
| **D9** | **Les newsletters Gmail classées sont réinjectées dans Raindrop, sous seuil** ([#94](https://github.com/slucky31/LoreAI/issues/94)). Création uniquement si `SuggestedCollection` correspond exactement à une collection existante **et** que `Action = ATester` ou `Priority = Haute`. | Deuxième dérogation contrôlée à « une source non-Raindrop n'écrit pas dans Raindrop » (ADR 0012), après la veille du lot 9 — toujours en **création**, jamais en modification. Tag d'origine obligatoire (`newsletter`), flag dédié `false` par défaut. `Feed` **non concerné** (voir D11). Lot 13 |
| **D10** | **L'alerte Discord immédiate `ATester`/`Haute` est retirée** ([#64](https://github.com/slucky31/LoreAI/issues/64)). | Supprime `IImmediateNotifier`, `DiscordNotifier`, `INotificationPolicy`, `DefaultNotificationPolicy` et le compteur `Notified` de `CycleRuns`. Le filet « rien ne se perd » est désormais assuré par la file de lecture hebdomadaire (L1/L5) et le compte-rendu de cycle (O1), tous deux livrés — la justification d'origine de l'ADR 0005 ne tenait plus depuis l'ADR 0013. Lot 12 |
| **D11** | **La classification des flux RSS personnels est retirée** ([#99](https://github.com/slucky31/LoreAI/issues/99), déjà instruite). Miniflux reste le lecteur humain et le moteur RSS de la veille. | Confirme la règle générale : **on ne branche jamais un pipeline payant sur une source non bornée sans plafond.** « Non trié » est borné par les sauvegardes humaines ; un abonnement RSS ne l'est pas. Lot 12 |

### Une règle de fin de lot, à valider

Proposition issue du constat F1, à confirmer avant de l'appliquer :

> **Un lot n'est pas terminé tant qu'il n'est pas actif en production.** La définition de « fini » passe de *« PR mergée, tests verts, roadmap à jour »* à *« … et le flag activé sur `mcm8`, avec une observation consignée dans l'état des lieux »*.

Sans cette règle, chaque lot ajoute un flag `false` de plus et l'écart livré/actif continue de croître. Avec elle, le rythme de livraison ralentit — c'est le prix, et il est assumé ou non explicitement.

## Nouveaux scénarios

Cotation homogène avec [`roadmap.md`](roadmap.md) : **V** = valeur, **E** = effort, sur 3. Le détail de chaque constat est dans la [critique fonctionnelle](critique-fonctionnelle.md).

| # | Scénario | V | E | Constat |
|---|---|---|---|---|
| O7 | **Sauvegarde réelle** — `pg_dump` planifié + rotation, `.env` chiffré hors du Pi | 3 | 1 | F9 |
| O8 | **Garde-fou budget dur** — `LlmCalls` + `ILlmBudgetGuard` avant appel | 3 | 2 | F2 |
| O9 | **Limiteur de débit Raindrop partagé** — dans le client, pas dans un job | 2 | 2 | F10 |
| O10 | **Journal d'identité au démarrage** — version, runtime, hôte, **jobs planifiés et leur cron** | 3 | 1 | F1, F15 |
| O11 | **Healthcheck du conteneur MCP** | 2 | 1 | F1 |
| O12 | **Renovate débloqué** — une virgule dans `renovate.json` | 2 | 1 | F8 |
| N6 | **Rétention** — purge glissante des tables de journal | 1 | 1 | F11 |
| C6 | **Newsletters Gmail → Raindrop, sous seuil** | 2 | 2 | D9 |
| L7 | **Apprentissage des corrections humaines** — taux de correction par collection, puis few-shot dans le prompt | 3 | 2 | F12 |
| S10 | **Exploitation des surlignages** — MCP `get_highlights`, injection dans la revue mensuelle | 3 | 1 | F4 |
| S11 | **Expansion de requête** avant d'envisager `pgvector` | 2 | 1 | — |
| S12 | **Export automatisé vers Obsidian** — variante 2 du pont, jamais écrite | 2 | 2 | — |

## Ordre de bataille

Six lots. L'ordre est contraint : **on ne construit rien de neuf tant qu'on peut tout perdre et dériver en coût sans le voir.**

### Lot 11 — Reprendre la main sur l'exploitation

Aucune valeur fonctionnelle visible, aucun appel LLM, aucun changement de schéma. C'est le lot qui rend le reste pilotable, et c'est celui qui doit passer en premier.

- **O7 — Sauvegarde** ([#37](https://github.com/slucky31/LoreAI/issues/37)) : `pg_dump` planifié de la base `loreai` avec rotation, restauration **testée une fois** (une sauvegarde jamais restaurée n'est pas une sauvegarde), et copie chiffrée du `.env` de `mcm8` hors de la machine. Les deux actifs sont distincts et se perdent différemment — voir F9.
- **O10 — Journal d'identité au démarrage** ([#65](https://github.com/slucky31/LoreAI/issues/65)) : version, runtime .NET, OS/architecture, hôte, **et la liste des jobs réellement planifiés avec leur expression cron**. Cette dernière partie n'est pas dans le ticket d'origine ; c'est elle qui ferme le trou F1 (« le projet ne sait pas ce qu'il fait tourner »). Code partagé dans `LoreAI.Infrastructure`, consommé par le Worker **et** le MCP — c'est le `StartupInfo` que `CLAUDE.md` décrit déjà mais qui n'a jamais été écrit (F15).
- **O11 — Healthcheck MCP** ([#68](https://github.com/slucky31/LoreAI/issues/68)) : le mécanisme de #35 ne se transpose pas (pas de `CycleRuns` pour un serveur sans état). « En bonne santé » = le serveur répond et la connexion `loreai_ro` est vivante.
- **O12 — Renovate** ([#40](https://github.com/slucky31/LoreAI/issues/40)) : virgule manquante ligne 5 de `renovate.json`. Vérifier ensuite que le Dependency Dashboard ([#9](https://github.com/slucky31/LoreAI/issues/9)) se repeuple.
- **[#97](https://github.com/slucky31/LoreAI/issues/97)** — logs de `--add-watch-topic` en 4 lignes distinctes. Embarqué ici parce que le lot touche déjà à la journalisation.
- **Ménage du backlog** : fermer les cinq issues livrées (#31, #35, #71, #73, #75) et les trois périmées (#33, #36, #39) — voir le triage ci-dessous. Corriger `CLAUDE.md` (F15).

⚠️ Une part de ce lot n'est pas du code mais de l'exploitation sur `mcm8` (cron de `pg_dump`, dépôt de la sauvegarde chiffrée). Pas d'accès SSH depuis le bac à sable : produire des commandes copiables, comme le veut la convention du projet.

### Lot 12 — Reprendre la main sur le coût

Le lot qui empêche le prochain #99.

- **D11 / [#99](https://github.com/slucky31/LoreAI/issues/99)** — retrait de la classification des flux RSS personnels. Périmètre de suppression déjà détaillé dans le ticket, y compris les points ouverts (sort de `SourceType.Feed` selon la présence de lignes en base, amendement du paragraphe lot 7 de `roadmap.md`).
- **O8 — Garde-fou budget** (D8) : table `LlmCalls` (job appelant, modèle, tokens entrée/sortie/cache, coût estimé, horodatage) écrite par **tous** les appelants LLM — `AnthropicClassifier`, `AnthropicEmailLinkExtractor`, `AnthropicTopicWatchFilter`, `AnthropicThemeNarrativeGenerator`. Elle unifie les trois sources de mesure aujourd'hui éclatées (F2) et devient la source de S6, qui n'a plus à parser du `jsonb`. Puis `ILlmBudgetGuard` dans `Core` : consulté au début de chaque job consommateur, il refuse et journalise au-delà du seuil `Classifier__MonthlyBudgetUsd`. Une seule alerte Discord au franchissement, jamais une par appel refusé.
- **[#34](https://github.com/slucky31/LoreAI/issues/34) — mesure du cache de prompt** : la table `LlmCalls` expose enfin `cache_creation_input_tokens` / `cache_read_input_tokens` en clair. La mesure de 30 minutes annoncée dans l'arbitrage devient une lecture de requête. **Trancher et fermer l'issue** — si les compteurs sont à zéro, le seuil de 4 096 tokens est confirmé et le sujet est clos jusqu'à un éventuel backfill.
- **D10 / [#64](https://github.com/slucky31/LoreAI/issues/64)** — retrait de l'alerte Discord immédiate.

Après ce lot, le coût est mesuré par job, plafonné, et la source non bornée est débranchée.

### Lot 13 — Écritures externes : fiabilité et cohérence

Trois écritures hors périmètre historique coexistent désormais (veille → Raindrop, file de lecture → tag Raindrop, et bientôt newsletters → Raindrop). Ce lot les met au même standard.

- **O9 — Limiteur de débit Raindrop partagé** ([#86](https://github.com/slucky31/LoreAI/issues/86)) : au niveau de `RaindropClient`, pas dans `ReconciliationJob`. Six jobs partagent un token et plusieurs crons se croisent volontairement (dimanche 3h/4h/4h05) — un pacing dans un seul job ne protège de rien (F10).
- **C6 / [#94](https://github.com/slucky31/LoreAI/issues/94)** — newsletters Gmail → Raindrop sous seuil (D9). Flag dédié `false` par défaut, tag `newsletter`, création jamais modification.
- **[#98](https://github.com/slucky31/LoreAI/issues/98)** — marquer les entrées Miniflux de veille comme lues après évaluation. Dérogation ciblée à l'invariant ADR 0012, à documenter : il protège une catégorie **de lecture humaine**, pas une catégorie automatique que personne ne lit (F14).
- **N6 — Rétention** : purge glissante de `CycleRuns`, `WatchEvaluationLogs`, `EmailExtractionLogs`. Une fois `LlmCalls` en place (lot 12), `ClassificationRawResponse` peut être réduit au-delà de 90 jours — la mesure de coût ne dépend plus de lui.
- **[#92](https://github.com/slucky31/LoreAI/issues/92)** — harmonisation des logs entre les trois conteneurs. La partie horaire est déjà faite (#71, O4, livré) ; il reste le **format** commun et le cas de `miniflux`, qui a sa propre journalisation et ne se reformate pas depuis LoreAI. Requalifier le ticket sur ce qui reste réellement à faire.

### Lot 10 — Déduplication inter-sources (**C5**, [#51](https://github.com/slucky31/LoreAI/issues/51))

**Numéro et contenu inchangés** — l'issue existe sous ce nom et le renuméroter ne ferait que casser les références. Seule sa **position dans l'ordre** change : il passe après les trois lots de consolidation.

Réutilise la normalisation d'URL de `DuplicateUrlDetector` (N1, déjà écrite) comme clé de rapprochement : un `Item` canonique, les autres rattachés comme occurrences, jamais de reclassification de ce qui l'a déjà été.

Une nuance issue de D11 : avec la classification des flux personnels retirée, il ne reste plus que **trois** producteurs d'items classés (Raindrop, Newsletter, Veille) au lieu de quatre. Le besoin diminue d'autant — à réévaluer sur des chiffres réels (combien de doublons inter-sources le rapport hebdomadaire remonte-t-il aujourd'hui ?) **avant** d'attaquer le lot. C'est le seul lot de cette phase dont l'utilité mérite d'être vérifiée avant d'être construite.

### Lot 14 — Refermer la boucle

Le premier lot de cette phase qui crée de la valeur nouvelle. Les deux scénarios exploitent des données **déjà collectées**, sans rien ajouter à l'ingestion.

- **L7 — Apprentissage des corrections humaines** (F12). Deux étapes, la première ayant de la valeur seule :
  1. **Mesurer** : `ReconciliationJob` sait déjà qu'un article a été déplacé ou retagué après classification. En dériver un taux de correction par collection suggérée, exposé dans le rapport hebdomadaire. Réponse enfin factuelle à « est-ce que la classification est bonne ? », question à laquelle le projet ne sait pas répondre aujourd'hui.
  2. **Corriger** : injecter les N corrections récentes comme exemples dans le prompt de classification. ⚠️ Interaction directe avec #34 — ajouter des exemples grossit le préfixe et peut faire franchir le seuil de cache de 4 096 tokens, ce qui change l'arbitrage. À traiter avec la mesure du lot 12 en main, pas avant.
- **S10 — Surlignages** (F4) : outil MCP `get_highlights`, et injection des surlignages dans la revue mensuelle, où ils remplacent avantageusement un extrait tronqué. Zéro collecte à ajouter — la donnée est en base depuis le lot 1.

À décider dans ce lot : **que fait-on de `important` et `broken`, à zéro sur 1 369 items** (F5) ? Soit on retire la section liens morts du rapport et `BrokenTrackedArticlesAnalyzer`, soit on sonde réellement les URLs — c'est-à-dire le crawler que N3 voulait éviter. Ne pas laisser un analyseur tourner pour produire du vide.

### Lot 15 — Second cerveau

La promesse d'origine encore non tenue : le corpus est interrogeable, il n'est pas encore *utilisé*.

- **S11 — Expansion de requête** : mesurer d'abord si la recherche `tsvector` française échoue vraiment sur la synonymie et le mélange FR/EN. Un appel LLM court qui étend la requête avant recherche coûte bien moins qu'un embedding du corpus entier. **Décision `pgvector` reportée après cette mesure** — et rappel : le coût récurrent de génération des vecteurs reste entier, même si l'extension est disponible sans dépendance (D7).
- **S12 — Export automatisé vers Obsidian** : la variante 2 du pont, jamais écrite. `tool_card` et `export_item` existent côté MCP mais restent manuels, donc peu utilisés. Un petit CLI côté PC (jamais côté Pi — D4) qui interroge le MCP et écrit les `.md`, déclenché par une tâche planifiée Windows. Les fiches sont **régénérées, jamais éditées** ; les annotations humaines vivent dans un fichier voisin.

---

## Triage complet des issues ouvertes

Les 22 issues ouvertes au 2026-08-30, avec un verdict pour chacune.

### À fermer — déjà livré (5)

| Issue | Preuve dans le code |
|---|---|
| [#31](https://github.com/slucky31/LoreAI/issues/31) Compte-rendu de cycle Discord | `DiscordCycleReportNotifier`, règle « pas d'import, pas de notification » appliquée |
| [#35](https://github.com/slucky31/LoreAI/issues/35) Healthcheck Docker/Portainer | `HEALTHCHECK` en forme exec, `src/LoreAI.Worker/Dockerfile:58` |
| [#71](https://github.com/slucky31/LoreAI/issues/71) Logs en heure de Paris | `zoneinfo` copié depuis l'étage build + `ENV TZ=Europe/Paris`, dans les deux Dockerfiles |
| [#73](https://github.com/slucky31/LoreAI/issues/73) Lien projet dans la base d'outils | `toolUrl` dans le schéma du tool `classify`, migration `AddToolUrl` |
| [#75](https://github.com/slucky31/LoreAI/issues/75) Déclenchement manuel des jobs lents | `--run-weekly-insights` / `--run-monthly-review`, `Program.cs:214-226` |

> ⚠️ Fermer #35 laisse ouverte la question **autoheal**, qui y était rattachée. Recommandation : ne pas ouvrir de ticket pour l'instant — `CycleRuns` existe désormais pour mesurer la fréquence réelle des blocages, et rien n'a été observé en trois mois. À reconsidérer sur données, dans six mois.

### À fermer — périmé (3)

| Issue | Pourquoi |
|---|---|
| [#33](https://github.com/slucky31/LoreAI/issues/33) Comparaison Versionize | Corps vide, et Versionize fonctionne — 12 releases automatiques jusqu'à 0.20.0. Rien à trancher |
| [#36](https://github.com/slucky31/LoreAI/issues/36) Renommage de conteneur | Le compose actuel déclare `loreai-worker`, `loreai-mcp`, `miniflux`. Sans objet |
| [#39](https://github.com/slucky31/LoreAI/issues/39) Déploiement Pi | Décrit l'édition manuelle de `image:` et `/data/raindropai.db`, disparu depuis l'ADR 0009. Le déploiement est documenté et se fait par `pull` GHCR |

### Loties (11)

| Issue | Lot | Note |
|---|---|---|
| [#37](https://github.com/slucky31/LoreAI/issues/37) Sauvegarde | **11** | Requalifier : deux actifs distincts (base + `.env`), restauration à tester |
| [#65](https://github.com/slucky31/LoreAI/issues/65) Version/env au démarrage | **11** | Étendre au **listing des jobs planifiés** — c'est ce qui ferme F1 |
| [#68](https://github.com/slucky31/LoreAI/issues/68) Healthcheck MCP | **11** | |
| [#40](https://github.com/slucky31/LoreAI/issues/40) Renovate cassé | **11** | Virgule manquante, `renovate.json:5` |
| [#97](https://github.com/slucky31/LoreAI/issues/97) Logs `--add-watch-topic` | **11** | |
| [#99](https://github.com/slucky31/LoreAI/issues/99) Retrait classification RSS perso | **12** | Périmètre déjà instruit dans le ticket |
| [#34](https://github.com/slucky31/LoreAI/issues/34) Cache de prompt | **12** | Devient une lecture de `LlmCalls`. À **trancher et fermer** dans le lot |
| [#64](https://github.com/slucky31/LoreAI/issues/64) Alerte immédiate | **12** | Décidé : retrait (D10) |
| [#86](https://github.com/slucky31/LoreAI/issues/86) Throttle Raindrop | **13** | Requalifier : le limiteur va dans `RaindropClient`, pas dans `ReconciliationJob` |
| [#94](https://github.com/slucky31/LoreAI/issues/94) Newsletters → Raindrop | **13** | Décidé : oui, sous seuil (D9) |
| [#98](https://github.com/slucky31/LoreAI/issues/98) Marquer les entrées de veille lues | **13** | |

### À requalifier (2)

| Issue | Ce qu'il en reste |
|---|---|
| [#92](https://github.com/slucky31/LoreAI/issues/92) Harmoniser les logs des 3 conteneurs | La partie horaire est faite (#71). Reste le format commun, et le cas de `miniflux` qui ne se reformate pas depuis LoreAI. Lot 13 |
| [#51](https://github.com/slucky31/LoreAI/issues/51) Lot 10 — déduplication | Garde son numéro et son contenu, **repositionné après les lots 11-13**. Utilité à revérifier sur chiffres réels avant d'attaquer |

### Laissée telle quelle (1)

[#9](https://github.com/slucky31/LoreAI/issues/9) Dependency Dashboard — issue technique de Renovate, ne se ferme pas. À surveiller après le correctif O12 : si elle ne se repeuple pas, le correctif n'a pas pris.

### Issues à ouvrir

Ces scénarios n'ont pas encore de ticket :

| # | Titre proposé | Lot |
|---|---|---|
| O8 | Garde-fou budget LLM — table `LlmCalls` + `ILlmBudgetGuard` avant appel | 12 |
| N6 | Rétention des tables de journal (`CycleRuns`, `WatchEvaluationLogs`, `EmailExtractionLogs`) | 13 |
| L7 | Apprentissage des corrections humaines — mesure puis few-shot | 14 |
| S10 | Exploitation des surlignages — MCP `get_highlights` + revue mensuelle | 14 |
| — | Trancher le sort de `important`/`broken` (0 sur 1 369 items) | 14 |
| S11 | Expansion de requête avant d'envisager `pgvector` | 15 |
| S12 | Export automatisé vers Obsidian (CLI côté PC) | 15 |
| A1 | Newsletters : décoder les segments base64 des URLs de tracking avant le filtre de bruit — un lien `preferences-confirmed` est classé `Haute` et 2ᵉ de la file de lecture | 13 |
| A2 | Temps de lecture constant à 1 min en tête de file : mesurer le taux d'échec de `ContentStatus` par domaine (`lnkd.in` en tête) | 14 |
| A3 | Catalogue d'outils : `status` reste « À évaluer » pour les 21 outils — retirer le champ, ou livrer Q3 pour le renseigner | 14 |

Les trois dernières sont des anomalies **observées sur des données réelles** le 2026-08-30, détaillées dans [`reste-a-tester.md`](reste-a-tester.md#-anomalies-trouvées-en-vérifiant).

---

## Risques propres à cette phase

Les risques de [`roadmap.md`](roadmap.md#risques-et-points-de-vigilance) restent valables. Ceux-ci s'y ajoutent.

| Risque | Mitigation |
|---|---|
| **Trois lots sans valeur visible d'affilée** (11, 12, 13) — démotivant, et la tentation de sauter au lot 14 sera forte | Assumé et explicite : le lot 11 protège contre une perte irréversible, le 12 contre une facture. Aucun des deux ne se rattrape après coup. Le lot 14 n'est pas plus loin que 3 semaines de rythme observé |
| **Le garde-fou budget coupe le pipeline au mauvais moment** — un plafond atteint le 20 du mois arrête la classification pour 10 jours | Le garde-fou ne coupe que les **appels LLM**, jamais l'ingestion ni la persistance : les articles continuent d'entrer, ils sont classés en repli et rattrapables. Alerte au franchissement, seuil configurable, et `Worker__WriteBackToRaindrop` reste indépendant |
| **`LlmCalls` devient une troisième source de vérité du coût** à côté du `jsonb` existant | Migration complète dans le même lot : S6 lit `LlmCalls` et **cesse** de parser `ClassificationRawResponse`. Deux sources coexistantes seraient pire que l'état actuel |
| **L7 étape 2 casse l'arbitrage du cache de prompt** — les exemples grossissent le préfixe | C'est aussi une opportunité (franchir enfin le seuil de 4 096 tokens). À traiter avec la mesure du lot 12 en main. Ne pas faire L7-2 avant que #34 soit tranchée |
| **Le retrait de l'alerte immédiate (D10) rouvre le trou « rien ne se perd »** | Contrairement à l'analyse d'août, le filet existe désormais : file de lecture hebdomadaire (L1/L5) et compte-rendu de cycle, tous deux livrés. Si le manque se fait sentir, `INotificationPolicy` est réintroductible — mais on attend le manque, on ne l'anticipe pas |
| **La règle « un lot fini = un lot actif » ralentit la livraison** | C'est l'objectif. Le rythme actuel produit des lots que personne n'active — et donc de la valeur nulle malgré le travail fait |
| **La sauvegarde n'est pas testée** | Une restauration réelle est **dans** le périmètre du lot 11, pas un « à faire plus tard ». Une sauvegarde jamais restaurée n'est pas une sauvegarde |

## Questions ouvertes

- **La règle « un lot fini = un lot actif »** — à valider ou refuser explicitement. Elle change la définition de terminé pour tous les lots suivants.
- **Utilité réelle du lot 10 (déduplication)** — à mesurer sur le rapport hebdomadaire actuel avant de l'attaquer. Avec le retrait des flux personnels, il ne reste que trois producteurs d'items.
- **Sort de `important` / `broken`** — retirer l'analyseur, ou sonder réellement les URLs ? À trancher au lot 14.
- **`pgvector`** — reste conditionné à l'échec mesuré de S11, jamais ouvert par anticipation.
- **Autoheal** — laissé sans ticket, à reconsidérer sur données `CycleRuns` dans six mois.
- **Abonnement Claude en mode headless** — inchangé depuis la phase 1 : faisabilité établie, conditions d'usage non vérifiées, enjeu de quelques euros. Ne pas loter.
- **« Hermes »** — toujours non identifié (question ouverte héritée de la phase 1).
