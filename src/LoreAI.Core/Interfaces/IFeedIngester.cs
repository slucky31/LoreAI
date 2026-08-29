namespace LoreAI.Core.Interfaces;

/// <summary>Marqueur vide (lot 7, #48), même patron que <see cref="IGmailIngester"/> : permet à l'appelant de demander spécifiquement l'ingesteur RSS/Miniflux sans ambiguïté DI.</summary>
public interface IFeedIngester : ISourceIngester
{
}
