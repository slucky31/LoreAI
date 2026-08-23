# 0013 — Retrait du canal email

## Statut
Acceptée — remplace le volet email de l'[ADR 0005](0005-canaux-notification.md) ; le volet Discord de cette ADR reste valable. Exécute la décision D3 de la [roadmap](../roadmap.md).

## Contexte

Le digest email quotidien (`EmailNotifier`/MailKit, `DigestNotificationJob`) envoyait chaque jour la liste exhaustive des articles non encore couverts par une alerte Discord immédiate. En pratique, ce digest n'apportait rien : une liste d'articles déjà rangés automatiquement, sans action à prendre. La roadmap l'a acté (D3) — l'email disparaît complètement, sans canal de remplacement mail.

Retirer l'email supprime aussi le filet « rien ne se perd » qu'assurait le digest exhaustif : jusqu'ici, un article qui ne déclenchait pas d'alerte Discord réapparaissait de toute façon le lendemain. Sans lui, un cycle qui échoue silencieusement (ex. classification en repli) ne laissait plus aucune trace visible. C'est pourquoi l'ordre d'exécution du lot 2 impose le compte-rendu de fin de cycle Discord ([O1, #31](https://github.com/slucky31/LoreAI/issues/31)) *avant* ce retrait, pas après — livré séparément, PR précédente.

## Décision

Suppression complète et sans remplacement direct du canal email :
- `IDigestNotifier`, `EmailNotifier`, `EmailOptions`, `DigestMessageBuilder`, `SmtpSecurityResolver`/`SmtpSecurity`, `DigestNotificationJob` et le modèle `ClassifiedArticle` (qui n'existait que pour ce chemin) sont supprimés.
- La dépendance NuGet `MailKit` est retirée de `Directory.Packages.props` et de `LoreAI.Infrastructure.csproj`.
- La colonne `Articles.EmailDigestSentAtUtc` (et son index) est supprimée par migration EF Core (`RemoveEmailDigest`) — `IArticleRepository` perd `GetUnsentDigestItemsAsync`/`MarkDigestSentAsync`, qui n'avaient plus d'appelant.
- Toute la configuration `Email__*` et `Worker__DigestCronExpression` disparaît de `.env.example`, `appsettings.json` et de la doc de déploiement.

Aucun canal ne remplace le digest immédiatement. Le filet de sécurité qu'il assurait est repris par O1 (compte-rendu de fin de cycle Discord, déjà livré) pour la visibilité opérationnelle, et par L1 (file de lecture hebdomadaire, roadmap) pour la valeur de lecture — voir roadmap, axe « Lire ».

## Conséquences

- Zéro nouvelle dépendance, une dépendance de moins (`MailKit`), cohérent avec l'esprit du lot 2 (« hygiène »).
- Le compte-rendu de fin de cycle Discord (O1) devient le seul signal périodique que le worker traite effectivement des articles — le healthcheck Docker (#35) reste le signal que le worker *tourne*, une responsabilité distincte (voir doc-comment d'`ICycleReportNotifier`).
- `docs/adr/0005-canaux-notification.md` et `docs/adr/0006-stack-technique.md` gardent leur texte d'origine (dossier historique) mais leur ligne **Statut** est mise à jour pour renvoyer ici.
- Un futur canal de restitution périodique (ex. L1, S4 dans la roadmap) partira d'une page blanche plutôt que d'adapter le digest existant — assumé, le format « liste exhaustive » n'était de toute façon pas celui visé par ces scénarios (Markdown, pas HTML email).
