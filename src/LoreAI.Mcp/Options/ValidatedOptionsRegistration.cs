namespace LoreAI.Mcp.Options;

/// <summary>
/// Copie volontaire de l'équivalent dans <c>LoreAI.Worker</c> : chaque hôte reste maître de sa propre
/// racine de composition, et ce n'est que 12 lignes — pas de quoi justifier un projet partagé de plus
/// pour deux appelants. Enregistre une section de configuration en échouant au démarrage plutôt qu'au
/// premier appel MCP.
/// </summary>
internal static class ValidatedOptionsRegistration
{
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
