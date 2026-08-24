using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using LoreAI.Mcp.Options;

namespace LoreAI.Mcp.Security;

/// <summary>
/// Défense en profondeur derrière le réseau Tailscale (ADR 0010/0014) : le réseau limite qui frappe à la
/// porte, ce jeton reste la serrure. Comparaison en temps constant pour ne pas laisser fuir sa longueur
/// ou son préfixe via une attaque temporelle.
/// </summary>
public sealed class BearerTokenMiddleware
{
    private const string SchemePrefix = "Bearer ";
    private readonly RequestDelegate _next;

    public BearerTokenMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<McpOptions> mcpOptions)
    {
        var header = context.Request.Headers.Authorization.ToString();
        var providedToken = header.StartsWith(SchemePrefix, StringComparison.Ordinal)
            ? header[SchemePrefix.Length..]
            : null;

        var isAuthorized = providedToken is not null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(providedToken),
            Encoding.UTF8.GetBytes(mcpOptions.Value.BearerToken));

        if (!isAuthorized)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        await _next(context);
    }
}
