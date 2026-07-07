# 0001 — Architecture générale

## Statut
Acceptée

## Contexte
RaindropAI est un outil personnel qui classifie automatiquement les articles Raindrop.io et aide à identifier ce qu'il faut lire ou tester. Le volume et la complexité fonctionnelle sont faibles (un seul flux : polling → classification → persistance → notification). Il fallait choisir un niveau d'architecture .NET adapté sans sur-ingénierer.

## Décision
Solution .NET 9 multi-projets simple :
- `RaindropAI.Core` : modèles, enums et interfaces, zéro dépendance externe.
- `RaindropAI.Infrastructure` : implémentations concrètes (Raindrop, classification LLM, persistance SQLite, notifications).
- `RaindropAI.Worker` : Worker Service (Generic Host) qui compose les dépendances et exécute les jobs planifiés.

Explicitement **pas** de Clean Architecture/CQRS/MediatR : une séparation interfaces (Core) / implémentations (Infrastructure) suffit pour la testabilité recherchée, sans l'indirection d'un pattern plus lourd.

## Conséquences
- Code plus direct à lire et à faire évoluer pour un projet à un seul contributeur.
- Si le périmètre grossissait significativement (multi-utilisateurs, API exposée, etc.), cette architecture devrait être revue.
