# 0004 — Moteur de classification LLM

## Statut
Acceptée

## Contexte
Chaque nouvel article doit être classifié (catégorie, action recommandée, priorité, justification) par un LLM. Deux fournisseurs étaient envisagés : Claude Haiku (Anthropic) et GPT-4o-mini (OpenAI), avec un coût réel négligeable dans les deux cas au volume d'un usage personnel.

## Décision
`IClassifier` (dans `Core`) comme seule dépendance vue par le reste du code, avec `AnthropicClassifier` comme implémentation retenue (Claude Haiku), cohérente avec l'usage existant de Claude par l'utilisateur. Pas de SDK .NET officiel Anthropic disponible : l'implémentation consomme directement l'API Messages via `HttpClient` + `System.Text.Json`, avec **tool-use forcé** (`tool_choice: {"type": "tool", "name": "classify"}` + `input_schema` JSON Schema) pour garantir une sortie strictement structurée, plutôt que du texte libre à parser.

Une validation défensive (`ClassificationResponseParser`) reste appliquée même sur cette sortie contrainte (enums insensibles à la casse, tolérance aux fences ```json résiduels), avec repli (`ClassificationResult.Fallback`) en cas d'échec — un article n'est jamais perdu silencieusement.

## Conséquences
- Changer de fournisseur LLM (ex. GPT-4o-mini) ne nécessite qu'une nouvelle implémentation d'`IClassifier`, sans toucher au reste de l'application.
- `ClassificationRawResponse` est systématiquement conservé en base pour audit/debug.
