namespace LoreAI.Core.Interfaces;

/// <summary>Marqueur vide (lot 8, #49), même patron que <see cref="IRaindropClient"/> : permet à l'appelant de demander spécifiquement l'ingesteur Gmail sans ambiguïté DI avec un futur ingesteur Feed.</summary>
public interface IGmailIngester : ISourceIngester
{
}
