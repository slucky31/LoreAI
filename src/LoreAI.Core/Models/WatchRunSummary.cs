namespace LoreAI.Core.Models;

/// <summary>Résultat d'une exécution de <c>TopicWatchJob</c> pour un sujet (lot 9, #50) — matière du digest Discord groupé.</summary>
public sealed record WatchTopicRunResult(string TopicName, int EvaluatedCount, int AddedCount);

/// <summary>
/// Résumé d'une exécution complète de la veille, tous sujets confondus. Remplace la notification détaillée
/// par article (décision de redesign en session) : un seul message Discord par run, même règle « pas
/// d'import, pas de notification » qu'O1/<c>CycleRun</c> — voir <c>IWatchDigestNotifier</c>.
/// </summary>
public sealed record WatchRunSummary(IReadOnlyList<WatchTopicRunResult> Topics);
