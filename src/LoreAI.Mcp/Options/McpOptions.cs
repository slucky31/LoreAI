using System.ComponentModel.DataAnnotations;

namespace LoreAI.Mcp.Options;

public sealed class McpOptions
{
    /// <summary>
    /// Défense en profondeur derrière le réseau Tailscale (ADR 0010/0014) : un seul opérateur, un seul
    /// client MCP, un jeton statique suffit — pas d'infrastructure OAuth.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public required string BearerToken { get; init; }
}
