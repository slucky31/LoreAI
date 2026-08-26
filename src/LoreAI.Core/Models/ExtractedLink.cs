namespace LoreAI.Core.Models;

/// <summary>Un lien retenu par <see cref="Interfaces.IEmailLinkExtractor"/> comme un vrai article, avec le titre proposé par le modèle à partir du contexte du mail.</summary>
public sealed record ExtractedLink(string Url, string Title);
