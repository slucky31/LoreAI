namespace LoreAI.Core.Models;

/// <summary>Sujet de veille défini en configuration (C4, lot 9, #50) — sert de contexte au LLM, pas de mapping vers un flux Miniflux particulier.</summary>
public sealed record WatchTopic(string Name, string Description);
