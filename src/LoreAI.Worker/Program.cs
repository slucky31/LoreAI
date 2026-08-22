using System.Globalization;
using Coravel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Services;
using LoreAI.Infrastructure.Classification;
using LoreAI.Infrastructure.Notifications;
using LoreAI.Infrastructure.Persistence;
using LoreAI.Infrastructure.Raindrop;
using LoreAI.Worker.Options;
using LoreAI.Worker.Resilience;
using LoreAI.Worker.Services;
using Serilog;

// Culture invariante sur les sinks : des logs rendus à l'identique quelle que soit la machine, et
// alignés sur le conteneur, qui tourne de toute façon en mode globalization-invariant.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Serilog est la seule source de vérité pour les niveaux de log : tout se règle sous `Serilog:`.
    // Une section `Logging:LogLevel` cohabitait auparavant, filtrée en amont par Microsoft.Extensions.Logging
    // alors que Serilog applique ensuite son propre minimum — deux réglages pour un seul effet attendu,
    // dont l'un pouvait rester sans effet visible.
    builder.Services.AddSerilog((services, loggerConfiguration) =>
    {
        var logFilePath = builder.Configuration["Serilog:FilePath"] ?? "logs/loreai-.log";
        loggerConfiguration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(logFilePath, rollingInterval: Serilog.RollingInterval.Day, formatProvider: CultureInfo.InvariantCulture);
    });

    // Validées au démarrage : une configuration incomplète doit arrêter le service tout de suite,
    // pas produire un worker qui tourne et échoue en silence toutes les 15 minutes.
    builder.Services
        .AddValidatedOptions<PostgresOptions>(builder.Configuration, "Postgres")
        .AddValidatedOptions<RaindropApiOptions>(builder.Configuration, "Raindrop")
        .AddValidatedOptions<ClassifierOptions>(builder.Configuration, "Classifier")
        .AddValidatedOptions<DiscordOptions>(builder.Configuration, "Discord")
        .AddValidatedOptions<EmailOptions>(builder.Configuration, "Email")
        .AddValidatedOptions<WorkerOptions>(builder.Configuration, "Worker")
        .AddValidatedOptions<NotificationOptions>(builder.Configuration, "Notification");

    // IDbContextFactory plutôt qu'AddDbContext : les repositories restent des singletons (inchangé),
    // et un DbContext n'est ni thread-safe ni fait pour être partagé sur la durée de vie du host.
    builder.Services.AddDbContextFactory<LoreAiDbContext>((serviceProvider, options) =>
    {
        var postgresOptions = serviceProvider.GetRequiredService<IOptions<PostgresOptions>>().Value;
        options.UseNpgsql(postgresOptions.ConnectionString);
    });
    builder.Services.AddSingleton<PostgresSchemaGuard>();
    builder.Services.AddHostedService<PostgresSchemaInitializer>();
    builder.Services.AddSingleton<IArticleRepository, ArticleRepository>();
    builder.Services.AddSingleton<IPollingStateRepository, PollingStateRepository>();
    // DefaultNotificationPolicy expose des seuils en paramètres de constructeur ; ils étaient annoncés
    // « injectables » mais aucun appelant ne les fournissait. On les alimente depuis la configuration ici,
    // plutôt que de faire dépendre Core de Microsoft.Extensions.Options.
    builder.Services.AddSingleton<INotificationPolicy>(serviceProvider =>
    {
        var notificationOptions = serviceProvider.GetRequiredService<IOptions<NotificationOptions>>().Value;
        return new DefaultNotificationPolicy(
            notificationOptions.TriggerActions.ToHashSet(),
            notificationOptions.MinimumPriority);
    });
    builder.Services.AddSingleton<IDigestNotifier, EmailNotifier>();

    builder.Services.AddHttpClient<IRaindropClient, RaindropClient>()
        .AddStandardResilienceHandler();

    // Seul l'appel LLM sort des valeurs par défaut, cf. ClassifierResilience.
    builder.Services.AddHttpClient<IClassifier, AnthropicClassifier>()
        .AddStandardResilienceHandler(ClassifierResilience.Configure);

    builder.Services.AddHttpClient<IImmediateNotifier, DiscordNotifier>()
        .AddStandardResilienceHandler();

    builder.Services.AddScheduler();
    builder.Services.AddTransient<UnsortedClassificationJob>();
    builder.Services.AddTransient<DigestNotificationJob>();

    var host = builder.Build();

    var workerOptions = host.Services.GetRequiredService<IOptions<WorkerOptions>>().Value;
    host.Services.UseScheduler(scheduler =>
    {
        scheduler.Schedule<UnsortedClassificationJob>()
            .Cron(workerOptions.PollingCronExpression)
            .RunOnceAtStart()
            .PreventOverlapping(nameof(UnsortedClassificationJob));

        scheduler.Schedule<DigestNotificationJob>()
            .Cron(workerOptions.DigestCronExpression)
            .PreventOverlapping(nameof(DigestNotificationJob));
    });

    host.Run();
}
// L'outillage `dotnet ef` (migrations, etc.) construit le host jusqu'à sa configuration puis l'interrompt
// volontairement ici pour en extraire le DbContext, sans jamais appeler host.Run() — un fonctionnement
// normal à laisser remonter tel quel, pas un crash à journaliser en fatal.
catch (HostAbortedException)
{
    throw;
}
catch (Exception ex)
{
    Log.Fatal(ex, "LoreAI.Worker s'est arrêté de façon inattendue.");

    // Sans cela le process sortait avec 0 : un échec de démarrage (configuration invalide, /data non
    // inscriptible) était indistinguable d'un arrêt normal pour Docker et l'outillage.
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
