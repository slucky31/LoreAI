namespace RaindropAI.Worker.Options;

/// <summary>
/// Enregistre une section de configuration en échouant au démarrage plutôt qu'au premier cycle.
/// <para>
/// Le mot-clé <c>required</c> sur les propriétés d'options est une garantie du <i>compilateur</i> : le
/// <c>ConfigurationBinder</c> instancie par réflexion et l'ignore complètement, laissant simplement la
/// valeur à <c>null</c>. Sans la validation explicite ci-dessous, un <c>.env</c> incomplet démarrait donc
/// sans broncher, puis échouait silencieusement à chaque cycle (401 avalé par le catch du job).
/// </para>
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
