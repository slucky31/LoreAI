namespace LoreAI.Core.Models;

/// <summary>Un domaine dominant sur la fenêtre de tendance (S3), ex. « 12 items sur github.com ce mois-ci ».</summary>
public sealed record DomainTrend(string Domain, int Count);

/// <summary>Un tag dominant sur la fenêtre de tendance (S3), ex. « 7 articles tagués mcp ce mois-ci ».</summary>
public sealed record TagTrend(string Tag, int Count);
