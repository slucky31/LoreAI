namespace LoreAI.Core.Models;

/// <summary>Article actuellement suivi comme tagué par L5 (« cette-semaine ») — de quoi savoir quoi retirer au prochain passage.</summary>
public sealed record ReadingQueueTaggedArticle(long ArticleId, string SourceId);
