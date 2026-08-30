namespace LoreAI.Core.Enums;

public enum SourceType
{
    Raindrop,
    Newsletter,
    Feed,

    /// <summary>Veille automatique sur sujets (C4, lot 9, #50) — flux de recherche Miniflux, curseur distinct de <see cref="Feed"/> (lecture personnelle).</summary>
    Watch,
}
