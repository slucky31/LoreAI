# Critique fonctionnelle — état réel de LoreAI

> Revue faite le **2026-08-30**, sur `main` à la version **0.20.0** (+ le correctif `7b83b5f`).
> Complément de [`etat-des-lieux.md`](etat-des-lieux.md) (où on en est) et de [`roadmap.md`](roadmap.md) (où on va) :
> ce document dit **ce qui ne va pas**, avec la preuve à chaque fois.
> Les suites concrètes sont loties dans [`roadmap-phase-3.md`](roadmap-phase-3.md).

## Méthode

Lecture de `src/` (14 700 lignes, 4 projets), des 22 issues ouvertes, du `.env.example`, des `Dockerfile` et du `docker-compose.yml`. Les chiffres de corpus viennent d'un appel réel au serveur MCP de production (`stats`, 2026-08-30) :

```
totalItems: 1369 · importantItems: 0 · brokenItems: 0 · lastIndexedAtUtc: 2026-08-30T12:26Z
```

Aucun constat ci-dessous n'est déduit de la documentation seule : chacun est adossé à un fichier, une issue ou une mesure.

---

## Ce qui marche, et qu'il faut arrêter de rediscuter

Il faut le dire avant la critique, parce que c'est l'essentiel et que ça ne se voit plus dans les tickets ouverts.

- **Le pipeline central tient.** `Item` générique, `ISourceIngester`, classification par tool-use forcé avec re-validation défensive, repli qui ne perd jamais un article, write-back conditionné à une correspondance exacte de collection vérifiée en code. C'est la partie la mieux conçue du projet et elle n'a jamais eu besoin d'être reprise depuis l'ADR 0012.
- **L'observabilité de base existe** : `CycleRuns`, healthcheck Docker, compte-rendu Discord de fin de cycle. Le trou « le worker ne sait rien de son dernier cycle », qui bloquait deux issues depuis le début, est fermé.
- **Le corpus est réellement constitué** : 1 369 items indexés, indexation fraîche de moins de 4 heures au moment de la revue. Le prérequis n°1 de la roadmap (« le corpus est presque vide ») n'est plus vrai — c'est la condition qui débloque tout le reste.
- **Le MCP est la bonne réponse au bon problème.** 11 outils en lecture seule, rôle `loreai_ro`, tailnet. Il fait de Claude Code le front-end du corpus sans une ligne d'interface à écrire.
- **La chaîne de livraison est saine** : Conventional Commits → Versionize → image multi-arch sur GHCR, le Pi ne fait que `pull`. 431 tests verts.

La critique qui suit ne remet aucun de ces points en cause.

---

## Constats

Gravité : 🔴 à traiter en priorité · 🟠 à planifier · 🟡 dette froide.

### F1 🔴 — L'écart entre « livré » et « actif » n'est plus tenable

Quatre fonctionnalités sur les six dernières livrées sont **désactivées par défaut** et leur état réel en production n'est pas connu :

| Flag | Défaut | Fonctionnalité | État réel sur `mcm8` |
|---|---|---|---|
| `Worker__EmailIngestionEnabled` | `false` | Newsletters Gmail (lot 8) | inconnu |
| `Worker__FeedIngestionEnabled` | `false` | Flux RSS perso (lot 7) | inconnu |
| `Worker__ReadingQueueTaggingEnabled` | `false` | File de lecture taguée (L5) | inconnu |
| `Worker__TopicWatchEnabled` | `false` | Veille sur sujets (lot 9) | inconnu |

`etat-des-lieux.md` l'écrit lui-même : *« l'état exact des flags d'activation n'a pas été revérifié […] se fier au `.env` réel sur `mcm8`, pas à cette ligne »*. Autrement dit : **le projet ne sait pas ce qu'il fait tourner.**

Ce n'est pas un défaut de rigueur documentaire, c'est un défaut de conception : rien dans le produit ne dit son propre état de configuration. Le worker démarre, planifie ou non des jobs selon des booléens, et n'en journalise jamais la synthèse. C'est exactement ce que demande [#65](https://github.com/slucky31/LoreAI/issues/65), qui est ouvert depuis le 24 août et n'a jamais été loti.

Conséquence secondaire, plus gênante : **chaque nouveau lot ajoute un flag `false` par défaut**, donc un lot livré est un lot qui ne produit rien tant qu'une action manuelle non tracée n'a pas eu lieu sur le Pi. Le rythme de livraison a dépassé le rythme d'activation.

### F2 🔴 — Le coût LLM n'est mesuré qu'a posteriori, une fois par semaine

`LlmUsageAnalyzer` (S6) fait ce qu'on lui demande : il agrège les tokens du mois courant depuis `Articles.ClassificationRawResponse` et estime le coût. Mais c'est **un constat hebdomadaire, jamais un garde-fou** — aucun code du projet ne peut refuser un appel LLM parce que le budget est dépassé.

Le lot 7 en a fait la démonstration grandeur nature : la classification de tous les flux RSS personnels a été livrée sans que son coût soit modélisé (le tableau de coût de la roadmap ne couvre que « Non trié »), et [#99](https://github.com/slucky31/LoreAI/issues/99) chiffre après coup ~36 $/mois à 100 entrées/jour — **3,5× le budget de référence de 10 €/mois**, découvert par lecture de code, pas par une alerte.

Trois angles morts s'ajoutent :

1. **La mesure est incomplète.** S6 ne lit que `Articles.ClassificationRawResponse`. Les appels d'extraction de liens de newsletter (`EmailExtractionLogs`) et de filtrage de veille (`WatchEvaluationLogs`) sont facturés mais comptés ailleurs, ou pas comptés du tout.
2. **La granularité est fausse.** Le rapport donne un total mensuel, pas un coût par job. Impossible de répondre à « qu'est-ce qui coûte cher ? » — la question que pose précisément #99.
3. **Le cache de prompt reste non mesuré** ([#34](https://github.com/slucky31/LoreAI/issues/34)). L'arbitrage de la roadmap est solide sur le papier (seuil 4 096 tokens Haiku vs préfixe stable ~900), mais il repose sur une estimation de tokens, jamais sur une lecture de `cache_creation_input_tokens`. La mesure était annoncée à 30 minutes ; elle n'a pas été faite.

### F3 🟠 — La classification des flux RSS personnels ne produit rien d'exploitable

C'est le sujet de [#99](https://github.com/slucky31/LoreAI/issues/99), déjà instruit et décidé. Je le confirme à la lecture du code : `FeedIngestionJob` classe chaque entrée, persiste, et **n'écrit jamais nulle part** (invariant ADR 0012). La seule valeur produite est une alerte Discord occasionnelle et l'alimentation du catalogue d'outils.

Le vrai défaut n'est pas le coût, c'est le **cadrage de la source** : « Non trié » est borné par ce que l'humain choisit de sauvegarder ; un abonnement RSS est borné par ce que les éditeurs publient. Brancher un pipeline payant sur une source non bornée sans plafond était l'erreur, pas le connecteur lui-même.

### F4 🟠 — Les surlignages sont collectés et jamais lus

`LibraryItem.HighlightsJson` est mappé, stocké en `jsonb`, et **aucun code ne le relit** (vérifié : les seules occurrences hors migrations sont la définition d'entité, le mapping du repository, et un commentaire disant que `LibraryItemSummary` ne les inclut pas).

La roadmap disait pourtant, au lot 1 : *« les surlignages sont la matière première idéale pour la synthèse, et ils sont perdus à chaque cycle »*. Ils ne sont plus perdus — ils sont archivés sans usage. Or c'est la donnée la plus dense du corpus : un surlignage est un jugement humain explicite, plus fiable qu'un tag et infiniment plus fiable qu'un score LLM.

### F5 🟠 — La détection de liens morts repose sur un signal vide

`importantItems: 0` et `brokenItems: 0` sur **1 369 items**. Les deux champs sont correctement mappés depuis l'API (`RaindropClient.cs:234-235`) : ce n'est pas un bug de code, c'est que Raindrop ne renseigne pas `broken` sur ce compte et que la fonction « important » n'est pas utilisée.

Conséquence : **N3 (liens morts) et la section correspondante du rapport hebdomadaire ne produiront jamais rien**, et `BrokenTrackedArticlesAnalyzer` tourne pour rien. Le scénario avait été coté « effort 2, deux lignes de mapping, évite d'écrire un crawler » — l'économie était réelle, mais elle achetait un champ qui reste à zéro. Soit on assume qu'il n'y a pas de liens morts, soit il faut réellement sonder les URLs, ce qui est le crawler qu'on avait voulu éviter.

### F6 🟠 — Cinq issues livrées sont toujours ouvertes

Vérifié dans le code, pas dans la doc :

| Issue | Prétendument à faire | Réalité |
|---|---|---|
| [#31](https://github.com/slucky31/LoreAI/issues/31) Compte-rendu de cycle Discord | ouvert | **livré** — `DiscordCycleReportNotifier`, règle « pas d'import, pas de notification » appliquée |
| [#35](https://github.com/slucky31/LoreAI/issues/35) Healthcheck Docker/Portainer | ouvert | **livré** — `HEALTHCHECK` en forme exec, `Worker/Dockerfile:58` |
| [#71](https://github.com/slucky31/LoreAI/issues/71) Logs en heure de Paris | ouvert | **livré** — `zoneinfo` copié depuis l'étage build + `ENV TZ=Europe/Paris`, dans les **deux** Dockerfiles |
| [#73](https://github.com/slucky31/LoreAI/issues/73) Lien projet dans la base d'outils | ouvert | **livré** — `toolUrl` dans le schéma du tool `classify`, migration `AddToolUrl`, parsing borné à 300 caractères |
| [#75](https://github.com/slucky31/LoreAI/issues/75) Déclenchement manuel des jobs lents | ouvert | **livré** — `--run-weekly-insights` / `--run-monthly-review`, `Program.cs:214-226` |

Cinq faux positifs sur vingt-deux, soit **près d'un quart du backlog visible qui ment**. C'est ce qui rend la lecture des tickets ouverts inutilisable comme état des lieux, et c'est aussi ce qui fait qu'on relit la roadmap au lieu de regarder GitHub.

### F7 🟡 — Trois issues sont périmées

- [#36](https://github.com/slucky31/LoreAI/issues/36) « Renommer `raindropai-config-raindropai-1` » — le `docker-compose.yml` actuel déclare `loreai-worker`, `loreai-mcp`, `miniflux`. Sans objet.
- [#39](https://github.com/slucky31/LoreAI/issues/39) « Déploiement Pi » — décrit l'édition manuelle de `image:` et parle de `/data/raindropai.db`, qui n'existe plus depuis le passage à PostgreSQL (ADR 0009). Le déploiement est aujourd'hui documenté et se fait par `pull` d'image GHCR.
- [#33](https://github.com/slucky31/LoreAI/issues/33) « Comparaison Versionize avec repo comic » — corps vide, et Versionize fonctionne (12 releases automatiques, jusqu'à 0.20.0). Sans énoncé, il n'y a rien à trancher.

### F8 🔴 — Renovate est bloqué, et personne ne le voit

[#40](https://github.com/slucky31/LoreAI/issues/40) est ouvert depuis longtemps avec le message « Renovate will stop PRs until it is resolved ». La cause est dans `renovate.json`, ligne 5 : **une virgule manquante**.

```jsonc
"extends": [
  "config:recommended",
  "helpers:pinGitHubActionDigests"   // ← virgule absente
  ":label(renovate)",
```

Conséquence concrète : **aucune mise à jour de dépendance n'est proposée** sur un projet .NET 10 qui centralise pourtant ses versions dans `Directory.Packages.props` et épingle les digests d'actions GitHub. C'est le correctif au meilleur rapport valeur/effort de tout le backlog — un caractère.

### F9 🔴 — Il n'y a aucune sauvegarde

[#37](https://github.com/slucky31/LoreAI/issues/37) est ouvert avec pour tout contenu « Quelles solutions ? ». La roadmap avait pourtant posé la règle au lot 0 : *« Mettre en place la sauvegarde `pg_dump` planifiée **avant** de supprimer le `.db` »*. Le `.db` a été supprimé. Rien n'indique que le `pg_dump` existe.

Deux actifs sont exposés, et ils ne se sauvegardent pas de la même façon :

1. **La base `loreai`** — 1 369 items indexés, tout l'historique de classification, les réponses LLM brutes qui portent la mesure de coût, les curseurs de polling. Sa perte n'est pas rattrapable par un ré-import : les curseurs perdus déclenchent un backfill complet, donc une facture LLM.
2. **Le fichier `.env` sur `mcm8`** — token Raindrop, clé Anthropic, refresh token Google, jeton API Miniflux, jeton bearer MCP, mots de passe PostgreSQL. Aucun de ces secrets n'existe ailleurs. Le perdre, c'est reprovisionner sept identités à la main, dont un consentement OAuth Google interactif.

C'est, de loin, le risque le plus élevé du projet aujourd'hui — devant le coût LLM.

### F10 🟠 — Le token Raindrop est partagé par six jobs sans aucun pacing

[#86](https://github.com/slucky31/LoreAI/issues/86) documente une rafale observée le 2026-08-29 : 148 appels séquentiels de `ReconciliationJob` → 429, puis timeout Polly, puis ~25 × 403 (bannissement temporaire du token).

Le ticket propose un pacing **dans `ReconciliationJob`**. C'est la bonne intention au mauvais endroit. À la lecture de `Program.cs`, **six jobs partagent le même `IRaindropClient` et donc le même token** : `UnsortedClassificationJob` (toutes les 15 min), `LibraryIndexingJob`, `WeeklyInsightsJob`, `ReconciliationJob`, `ReadingQueueTaggingJob`, `TopicWatchJob`. Plusieurs de leurs crons se croisent volontairement (dimanche 3h / 4h / 4h05). Un limiteur posé dans un seul job ne protège pas des cinq autres.

Le bon niveau est le client HTTP : un limiteur de débit partagé dans `RaindropClient`, pas un `Task.Delay` dans une boucle de job.

### F11 🟠 — Aucune donnée n'est jamais purgée

Vérifié : aucun `Delete`, `Purge` ni politique de rétention dans les repositories.

| Table | Croissance | Justification actuelle |
|---|---|---|
| `CycleRuns` | ~96 lignes/jour, soit **~35 000/an** | le healthcheck n'en lit que **3** |
| `Articles.ClassificationRawResponse` | une réponse Anthropic complète par article | nécessaire à S6 — mais S6 ne regarde que le mois courant |
| `WatchEvaluationLogs` | une ligne par candidat de veille évalué | « log d'audit minimal » |
| `EmailExtractionLogs` | une ligne par mail traité | idem |

Rien n'est critique à l'échelle d'un an sur un Pi. Mais c'est une base **mutualisée** avec d'autres projets et Miniflux, sur un SSD partagé, et personne ne surveille sa taille — le risque n'est pas LoreAI qui grossit, c'est LoreAI qui gêne un voisin.

### F12 🟠 — Le signal le plus précieux du projet est capté puis jeté

C'est le constat le plus important de cette revue, et le seul qui parle de valeur plutôt que de dette.

`ReconciliationJob` (L3, lot 6) re-lit les articles suivis chez Raindrop et détecte que **l'humain a modifié les tags, déplacé l'article, ou l'a supprimé**. C'est littéralement une correction de la classification automatique, produite gratuitement par l'usage normal.

Ce signal sert aujourd'hui à trois choses : marquer l'article comme « traité », détecter les liens cassés, déclencher des relances. **Il ne sert jamais à améliorer la classification suivante.** Le prompt reconstruit la taxonomie à chaque cycle (ADR 0007), mais ne sait rien de ses propres erreurs passées : si tous les articles rangés dans « X » par le modèle finissent déplacés à la main vers « Y », le modèle recommencera indéfiniment.

Le projet a construit une boucle de retour complète… et ne l'a pas refermée.

### F13 🟡 — Un seul webhook Discord porte cinq notifieurs

`DiscordNotifier` (alerte immédiate), `DiscordCycleReportNotifier`, `DiscordWeeklyDigestNotifier`, `DiscordReportNotifier` (pièce jointe `.md`), `DiscordReminderNotifier`, `DiscordWatchDigestNotifier` — six implémentations, une seule `Discord__WebhookUrl`.

Tout arrive donc dans le même salon, sans hiérarchie : une alerte qui demande une action et un digest hebdomadaire de 34 articles périmés ont la même présence visuelle. C'est ce qui a rendu [#64](https://github.com/slucky31/LoreAI/issues/64) inévitable — le canal n'est pas bruyant à cause d'un notifieur en trop, il l'est parce qu'il n'y a **qu'un** canal.

### F14 🟡 — La veille laisse Miniflux se remplir indéfiniment

[#98](https://github.com/slucky31/LoreAI/issues/98), bien instruit. À noter comme conséquence de conception : l'invariant « une source Feed n'est jamais réécrite » (ADR 0012) a été posé pour une catégorie de **lecture humaine**, où il est juste. Appliqué à une catégorie de **veille automatique** que personne ne lit, il produit exactement l'effet inverse de son intention : du bruit accumulé sans propriétaire.

### F15 🟡 — `CLAUDE.md` décrit du code qui n'existe pas

`CLAUDE.md` cite `StartupInfo` comme exemple de code partagé à réutiliser depuis `LoreAI.Infrastructure`. **Aucune occurrence de `StartupInfo` dans `src/`.** La consigne (« ne jamais dupliquer les helpers ») reste bonne ; son exemple est faux, et c'est précisément le genre de détail qui fait perdre du temps à un agent qui cherche la classe avant de comprendre qu'elle n'existe pas. À rapprocher de #65, qui demande justement d'écrire cette journalisation de démarrage.

---

## Ce que je propose d'ajouter

Cotation homogène avec la roadmap : **V** = valeur, **E** = effort, sur 3. Le lotissement est dans [`roadmap-phase-3.md`](roadmap-phase-3.md).

### Corriger la trajectoire

| # | Proposition | V | E | Pourquoi |
|---|---|---|---|---|
| **O7** | **Sauvegarde réelle** — `pg_dump` planifié + rotation, et sauvegarde chiffrée du `.env` hors du Pi | 3 | 1 | F9. Le seul risque non rattrapable du projet |
| **O8** | **Garde-fou budget dur** — table `LlmCalls` unifiée (job, modèle, tokens in/out/cache, coût), `ILlmBudgetGuard` consulté **avant** chaque appel, coupure des jobs LLM + une alerte au franchissement | 3 | 2 | F2. Transforme S6 d'un constat hebdomadaire en une limite. Unifie au passage les trois sources de mesure éclatées |
| **O9** | **Limiteur de débit Raindrop partagé** — au niveau de `RaindropClient`, pas d'un job | 2 | 2 | F10. Un seul token, six appelants, des crons qui se croisent |
| **O10** | **Journal d'identité au démarrage** ([#65](https://github.com/slucky31/LoreAI/issues/65)) — version, runtime, hôte, **et la liste des jobs réellement planifiés avec leur cron** | 3 | 1 | F1 + F15. Rend l'état de configuration lisible dans les logs, au lieu de dépendre d'un `.env` non versionné |
| **O11** | **Healthcheck MCP** ([#68](https://github.com/slucky31/LoreAI/issues/68)) | 2 | 1 | F1. Le seul conteneur applicatif sans sonde |
| **O12** | **Renovate débloqué** ([#40](https://github.com/slucky31/LoreAI/issues/40)) | 2 | 1 | F8. Une virgule |
| **N6** | **Rétention** — purge glissante de `CycleRuns`, `WatchEvaluationLogs`, `EmailExtractionLogs` ; `ClassificationRawResponse` réduit au bloc `usage` au-delà de 90 jours | 1 | 1 | F11. Base mutualisée, voisins à respecter |

### Refermer la boucle et exploiter le corpus

| # | Proposition | V | E | Pourquoi |
|---|---|---|---|---|
| **L7** | **Apprentissage des corrections humaines** — mesurer le taux de correction (article déplacé/retagué après classification) par collection, puis injecter les N corrections récentes comme exemples dans le prompt de classification | 3 | 2 | F12. Le seul mécanisme qui rend l'outil meilleur avec le temps. Les données existent déjà (`ReconciliationJob`), rien à collecter |
| **S10** | **Exploitation des surlignages** — outil MCP `get_highlights`, injection dans la revue mensuelle, « ce que tu as surligné sur X » | 3 | 1 | F4. Donnée déjà stockée, jugement humain explicite, zéro collecte à ajouter |
| **S11** | **Expansion de requête avant embeddings** — la recherche est en `tsvector` français ; elle échoue sur la synonymie et sur le mélange FR/EN (« veille » ≠ « monitoring »). Un appel LLM court qui étend la requête coûte moins qu'un ré-embedding du corpus | 2 | 1 | À mesurer **avant** d'ouvrir `pgvector` : si l'expansion suffit, l'embedding devient inutile |
| **S12** | **Export automatisé vers Obsidian** — variante 2 du pont, jamais écrite : un CLI qui interroge le MCP et écrit les `.md` du vault, déclenché côté PC | 2 | 2 | S7/S8 existent en outils MCP, mais restent manuels — donc peu utilisés |
| **C6** | **Newsletters Gmail → Raindrop sous seuil** ([#94](https://github.com/slucky31/LoreAI/issues/94)) | 2 | 2 | Décidé en session : création seulement si la collection matche **et** que le signal est fort |

### Ce que je ne recommande pas

Autant fermer ces pistes explicitement, elles reviendront sinon.

- **Un outil MCP `ask_corpus` / du RAG côté serveur.** Claude Code fait déjà le RAG côté client avec `search_items` + `get_item`. Ajouter un LLM dans le MCP, c'est payer deux fois et perdre le contrôle du contexte.
- **Les embeddings `pgvector` maintenant.** Coût récurrent de génération sur 1 369 items + chaque nouvel item, pour un gain non démontré. S11 d'abord, mesure ensuite.
- **Un conteneur `autoheal`.** La question traîne depuis #35. Sur trois mois d'exploitation, aucun blocage nécessitant un redémarrage automatique n'a été rapporté. Sans fréquence observée, c'est de la complexité spéculative — `CycleRuns` existe désormais pour la mesurer, il suffit de la lire dans six mois.
- **Changer de fournisseur LLM.** L'arbitrage de la roadmap tient intégralement : quelques euros économisés contre la perte du tool-use forcé sur lequel repose toute la fiabilité.
- **Le mode headless via l'abonnement Claude.** Enjeu de quelques euros, changement de nature du projet, conditions d'usage non vérifiées. À laisser en question ouverte, pas à loter.
