# 0005 — Canaux de notification Discord + Email

## Statut
Acceptée — le volet Email est retiré par l'[ADR 0013](0013-retrait-canal-email.md) ; le volet Discord reste valable.

## Contexte
L'utilisateur souhaite être informé des articles à lire/tester sur un média qu'il consulte facilement, sans que l'outil ne devienne une source d'information supplémentaire à surveiller activement.

## Décision
Deux canaux, chacun avec un rôle unique :
- **Discord** (`IImmediateNotifier`) : alerte quasi temps réel, déclenchée uniquement pour les articles jugés prioritaires à tester (`Action == ATester && Priority == Haute` par défaut, seuils configurables via `INotificationPolicy`). Une panne d'envoi Discord est journalisée mais n'interrompt jamais le traitement du batch en cours.
- **Email** (`IDigestNotifier`, MailKit) : digest quotidien exhaustif de tout ce qui n'a pas encore été envoyé (`EmailDigestSentAtUtc IS NULL`), groupé par catégorie puis action recommandée.

## Conséquences
- Aucun article n'est jamais perdu : ce qui n'a pas déclenché d'alerte Discord réapparaît de toute façon dans le digest quotidien.
- Chaque notifieur reste simple (une seule responsabilité), au prix de deux intégrations à maintenir plutôt qu'une seule abstraction générique.
