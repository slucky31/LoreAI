# Ce qu'il reste à tester

> Établi le **2026-08-30**, à partir de la [revue fonctionnelle](critique-fonctionnelle.md) et d'une vérification réelle sur la production (`mcm8`, v0.20.0).
> Un lot n'est pas vérifié parce que ses tests unitaires passent : `dotnet test` prouve que le code fait ce qu'on a écrit, pas que la fonctionnalité produit quelque chose d'utile en conditions réelles.
> Précédent du même genre : commit `9802cd5`, « vérification partielle du lot 5 en production ».

## Comment tenir ce fichier

Une ligne se ferme par **une observation datée**, jamais par une lecture de code : *« vérifié le 2026-09-01 : le rapport est arrivé sur Discord, 4 sections, aucun repli »*. Une ligne dont le critère de réussite n'est pas écrit n'est pas testable — l'écrire est la moitié du travail.

Légende : ✅ vérifié en production · ⏳ en attente d'un déclenchement · ❓ jamais observé · 🔴 anomalie trouvée en vérifiant.

---

## Vérifié le 2026-08-30

Fait pendant la revue, via le serveur MCP de production et les logs.

| Élément | Observation |
|---|---|
| ✅ **Corpus indexé** (lot 1) | `stats` → 1 369 items, index frais de moins de 4 h (`2026-08-30T12:26Z`). L'indexation hebdomadaire tourne |
| ✅ **`search_items`, `find_similar`, `export_item`** (Q2, S5, S8) | Déjà vérifiés au lot 5 (commit `9802cd5`, 1 333 items) — toujours cohérents |
| ✅ **`stats`** (Q1) | Répond, valeurs plausibles |
| ✅ **`catalog_tools`** (S7) | **Nouveau** : 21 outils catalogués entre le 2026-08-24 et le 2026-08-30. C'était le point resté « à vérifier » du lot 5 — il est fermé |
| ✅ **`reading_queue`** (L1) | **Nouveau** : file scorée non vide, tri décroissant cohérent, `sourceType` renseigné |
| ✅ **Ingestion Gmail active en production** (lot 8) | Déduit d'une entrée `sourceType: "Newsletter"` datée du 2026-08-30 09:00 dans la file de lecture. **Un des quatre flags inconnus est donc levé** : `Worker__EmailIngestionEnabled=true` |
| ✅ **Logs en heure de Paris** (O4, #71) | Ligne de log de `--add-watch-topic` du 2026-08-30 horodatée `[15:38:57]`, soit CEST et non UTC |
| ✅ **`--add-watch-topic`** (lot 9) | Sujet `enterprise-brain` provisionné sur `mcm8` : collection Raindrop 74518640 + catégorie Miniflux 2 créées |
| ✅ **Péremption** (N4) | Observée au lot 7 : section « 34 articles périmés » dans le rapport hebdomadaire — c'est même ce volume qui a motivé #78 |

## 🔴 Anomalies trouvées en vérifiant

Trois défauts que seule l'observation de données réelles pouvait révéler. Aucun n'a de ticket.

### A1 — Un lien de désinscription est classé « Priorité Haute » et remonte en 2ᵉ position de la file de lecture

Entrée réelle de `reading_queue`, `sourceType = Newsletter`, capturée le 2026-08-30 :

```
Titre : "Claude Code for .NET Developers - Free Course"
URL   : https://dd573b8f.click.kit-mail3.com/.../aHR0cHM6Ly9hcHAua2l0LmNvbS9wcmVmZXJlbmNlcy1jb25maXJtZWQ=
```

Le segment final est du base64 : il décode en **`https://app.kit.com/preferences-confirmed`** — une page de confirmation de préférences d'abonnement, pas un article.

`EmailLinkNoiseFilter` exclut bien les motifs `unsubscribe` / `preferences`, mais il les cherche **en clair dans l'URL**. Ici le motif est encodé en base64 à l'intérieur d'un redirecteur de tracking, donc invisible au filtre — et l'extracteur LLM, qui ne voit que l'ancre (« Free Course »), n'avait aucune raison de le rejeter. Le lien a ensuite été classé `Priority = Haute` et occupe la 2ᵉ place d'une file de lecture censée être la meilleure recommandation de la semaine.

À vérifier avant de corriger : **combien d'entrées `Newsletter` du corpus sont dans ce cas ?** La correction (décoder les segments base64 des URLs avant d'appliquer le filtre) est triviale, mais son intérêt dépend du volume.

### A2 — Le temps de lecture estimé vaut 1 minute pour toute la tête de file

Les 5 premières entrées de `reading_queue` ont toutes `estimatedMinutes: 1`. Quatre d'entre elles sont des liens raccourcis `lnkd.in`.

Deux conséquences, qui se cumulent :

- **L2 ne produit rien d'exploitable** : le nombre de mots vient de `ContentText`, et la récupération de contenu échoue ou ne rapporte quasi rien sur ces domaines (redirection LinkedIn, contenu majoritairement rendu en JS). Le best-effort fonctionne comme prévu — l'article n'est pas perdu — mais la donnée qui en sort est constante.
- **Le score L1 perd un de ses trois facteurs** : « priorité × fraîcheur × temps de lecture » devient de fait « priorité × fraîcheur », puisque le troisième terme est identique partout. Le classement n'est pas faux, il est simplement moins informé qu'annoncé.

À mesurer : le taux réel de `ContentStatus` en échec par domaine. C'est une requête, pas un chantier.

### A3 — Le catalogue d'outils est en écriture seule

Les 21 outils catalogués ont **tous** `status = "À évaluer"` et `relatedArticleCount = 1`. Aucun n'a jamais changé d'état, aucun n'a jamais été rencontré deux fois.

Le champ `status` a donc un cycle de vie théorique que rien ne fait avancer : ni le pipeline (il n'y a pas d'événement « j'ai testé cet outil »), ni un outil MCP en écriture (Q3 n'a jamais été livré). S7 promettait « nom, catégorie, statut d'évaluation, articles liés, **verdict** » — la moitié de cette promesse est un champ mort.

Décision à prendre, pas un bug à corriger : soit retirer le champ, soit livrer le moyen de le renseigner (Q3, un outil MCP en écriture, seul cas où il serait justifié).

---

## Vérifications en attente — production

### ⏳ S4 — Revue mensuelle narrative (lot 5)

Le seul livrable du projet qui n'a **jamais tourné**. Premier passage cron prévu le **2026-09-01 05:00 UTC**.

- **Ne pas attendre le cron** : `--run-monthly-review` existe depuis le lot 6 (O5, #75) précisément pour ça.
- **Critère** : un fichier `.md` arrive en pièce jointe sur Discord, avec une section par thème, un narratif cohérent et aucun repli de parsing dans les logs.
- **À surveiller en même temps** : le coût. C'est l'appel LLM le plus lourd du projet (~30 000 tokens d'entrée par thème). C'est l'occasion de mesurer avant que le garde-fou du lot 12 n'existe.

### ⏳ S7 — `tool_card` (lot 5)

`catalog_tools` est vérifié ; la génération de fiche ne l'est pas.

- **Geste** : `tool_card` sur un outil du catalogue (ex. `Fabric`, `browser-use`).
- **Critère** : un Markdown complet, frontmatter valide, articles liés présents, directement écrivable dans le vault.

### ❓ L4 — Relances (lot 6)

`DiscordReminderNotifier` existe et n'a **jamais été observé**. Il lui faut un article `ATester`/`Haute` non traité depuis 14 jours.

- Le corpus en contient : la file de lecture montre des `Haute` capturés les 27 et 28 août → seuil atteignable vers le **10-11 septembre**.
- **Critère** : un message Discord de relance, une seule fois par article, pas une répétition quotidienne à chaque passage de `ReconciliationJob`. **C'est le vrai risque à vérifier** : le job est quotidien, et rien dans son nom ne garantit que la relance ne se répète pas.

### ❓ L5 — File de lecture taguée (lot 8)

État du flag `Worker__ReadingQueueTaggingEnabled` sur `mcm8` : inconnu. Cron : dimanche 4h05 UTC.

- **Critère de pose** : le tag `cette-semaine` apparaît dans Raindrop sur les articles de la file, **et sur eux seuls**.
- **Critère de retrait** (le plus important, jamais observé) : à la passe suivante, les articles sortis de la file **perdent** le tag — le code le fait (`toUntag`), mais un échec y est seulement journalisé et « repris à la prochaine passe ». Un retrait qui échoue en silence produit un tag qui s'accumule semaine après semaine.
- **Critère d'invariant** : ni la collection ni la note des articles tagués ne changent. C'est la première écriture du projet hors « Non trié » — la vérifier est le prix de la dérogation.

### ❓ Lot 9 — Veille automatique

Le sujet est provisionné, la chaîne complète n'a jamais tourné. Il manque deux gestes manuels :

1. Ajouter les flux RSS de recherche dans la catégorie Miniflux `enterprise-brain` (via l'UI Miniflux).
2. Passer `Worker__TopicWatchEnabled=true`, puis attendre le cron (toutes les 6 h).

- **Critère** : au moins un raindrop **créé** dans la collection du sujet, portant le tag `veille` + les tags proposés, et un digest Discord groupé (un message par exécution, pas un par article).
- **Critère négatif, tout aussi important** : aucun item existant de Raindrop n'est modifié. La veille crée, elle ne touche jamais à ce qui est déjà rangé (ADR 0012).
- **À observer aussi** : le coût par exécution, et le taux de rejet du filtre LLM. Un sujet mal cadré peut créer beaucoup de bruit dans une vraie collection Raindrop — c'est réversible, mais fastidieux.

### ❓ Flags d'activation — les trois inconnues restantes

`Worker__EmailIngestionEnabled` est levé (voir plus haut). Restent :

| Flag | Comment le vérifier sans SSH |
|---|---|
| `Worker__FeedIngestionEnabled` | `SELECT * FROM "Articles" WHERE "SourceType" = 'Feed' LIMIT 1` — sans objet après le lot 12 (D11), mais la requête conditionne le sort de l'enum `SourceType.Feed` (point ouvert de #99) |
| `Worker__ReadingQueueTaggingEnabled` | Présence du tag `cette-semaine` dans la taxonomie Raindrop |
| `Worker__TopicWatchEnabled` | `SELECT count(*) FROM "WatchEvaluationLogs"` |

Ces trois requêtes sont un **contournement** : le vrai correctif est O10 (lot 11), qui journalise les jobs planifiés au démarrage.

---

## Vérifications en attente — exploitation

Aucune de ces lignes n'est un test unitaire. Toutes sont des gestes sur `mcm8`, et aucune n'a jamais été faite.

| # | À vérifier | Critère de réussite | Pourquoi ça compte |
|---|---|---|---|
| E1 | **Restauration d'une sauvegarde** | Un `pg_restore` sur une base jetable redonne un corpus complet et des curseurs cohérents | Une sauvegarde jamais restaurée n'est pas une sauvegarde. Bloqué tant que O7 (lot 11) n'existe pas |
| E2 | **Healthcheck vu par Portainer** | Le conteneur `loreai-worker` affiche `healthy` | C'était l'objet même de #35 ; livré, jamais confirmé côté Portainer |
| E3 | **Bascule en `unhealthy`** | Arrêter PostgreSQL, ou attendre 45 min sans cycle → le conteneur passe `unhealthy` | Un healthcheck qui ne passe jamais au rouge n'est pas un healthcheck. **Le seul test qui prouve que la sonde discrimine** |
| E4 | **Démarrage sans la base** | Le worker démarre, journalise, réessaie — il ne meurt pas | Exigence explicite du lot 0 (« pas de `depends_on` vers une instance qu'il ne possède pas »). Jamais éprouvée en réel |
| E5 | **Redémarrage du Pi** | Les trois conteneurs remontent dans le bon ordre, le worker retrouve ses curseurs, aucun doublon ni backfill | `restart: unless-stopped` + réseau Docker externe + base mutualisée : trois choses qui peuvent se croiser au boot |
| E6 | **Expiration de clé Tailscale** | L'expiration est désactivée sur `mcm8` dans la console d'admin | Piège déjà vécu le 2026-08-04 : le nœud quitte le tailnet sans panne ni message. Toujours pas fait |
| E7 | **Rate limit Raindrop sous charge croisée** | Faire coïncider réconciliation et cycle de polling → aucun 429/403 | Rafale observée le 2026-08-29 (#86). À rejouer **après** le limiteur du lot 13, sinon on ne saura pas s'il sert |
| E8 | **`--health-check` en code de retour** | `echo $?` vaut 0 en bonne santé, 1 sinon | Testé unitairement (`HealthCheckModeTests`), jamais dans le conteneur réel |

---

## Trous de couverture automatisée

La suite est solide — 431 tests, 63 fichiers, tous les analyseurs purs et tous les jobs couverts. Les manques listés ci-dessous sont réels mais secondaires : **aucun ne justifie de retarder un lot**, et plusieurs disparaîtront d'eux-mêmes (les tests de `FeedIngestionJob`/`MinifluxIngester` sont supprimés au lot 12 avec leur code).

| Manque | Gravité | Note |
|---|---|---|
| `DiscordReminderNotifier` | 🟠 | Seul notifieur Discord sans test, et seul dont le comportement en production n'a jamais été observé non plus (L4 ci-dessus). Le seul manque de cette liste qui cumule les deux |
| `WatchEvaluationLogRepository`, `EmailExtractionLogRepository` | 🟡 | Les deux tables de journal les plus récentes ; tous les autres repositories sont testés |
| `LlmResponseTextSanitizer` | 🟡 | Couvert indirectement par les tests de parsing, jamais directement. C'est pourtant une défense contre du contenu non maîtrisé |
| `PostgresSchemaInitializer` | 🟡 | Exécuté à chaque démarrage, jamais testé |
| Modes CLI `--add-watch-topic`, `--run-weekly-insights`, `--run-monthly-review` | 🟡 | Seul `--health-check` a son test (`HealthCheckModeTests`). Les trois autres ne sont éprouvés qu'à la main. C'est aussi le chemin qui a produit le bug corrigé par `7b83b5f` |
| Cohérence modèle EF ↔ migrations | 🟡 | Rien ne détecte un `DbContext` modifié sans migration générée. Un test qui échoue si `dotnet ef migrations has-pending-model-changes` est vrai coûte peu et évite une panne au démarrage en production |
| Câblage DI de `Program.cs` | 🟡 | Aucun test ne vérifie que le conteneur résout tous les jobs. Une dépendance oubliée se découvre au démarrage sur le Pi, pas en CI |

## Ce qui ne peut pas être testé depuis l'environnement de développement

À rappeler pour ne pas le redécouvrir :

- **Le Shadow PC n'exécutera jamais la suite de tests** (`HCS_E_HYPERV_NOT_INSTALLED` : ni WSL2, ni Hyper-V, ni Docker). Tests et migrations depuis `Ubuntu-perso` sur `afl-it-ndu` uniquement.
- **Aucun accès SSH ou API au Pi depuis le bac à sable.** Toute vérification d'exploitation (E1 → E8) se fait par des commandes copiables fournies à l'utilisateur.
- **Docker est requis pour `dotnet test`** (`Testcontainers.PostgreSql`) : vérifier `docker info` avant de lancer, et le signaler plutôt que de réessayer.

---

## Ordre suggéré

1. **Maintenant, sans rien attendre** : `--run-monthly-review` (S4) et `tool_card` (S7) — deux commandes, deux lignes fermées, et une mesure de coût utile avant le lot 12.
2. **Ce week-end** : L5 au passage du dimanche 4h05, si le flag est actif.
3. **Vers le 10 septembre** : L4, quand le seuil de 14 jours sera atteint.
4. **Avec le lot 11** : E1 à E5 — la sauvegarde et le healthcheck se testent au moment où on les construit, jamais après.
5. **Après le lot 13** : E7, pour prouver que le limiteur de débit sert à quelque chose.
6. **Quand tu veux** : les trois anomalies A1-A3, qui méritent chacune un ticket avant d'être corrigées.
