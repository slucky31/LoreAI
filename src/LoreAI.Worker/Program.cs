using System.Globalization;
using System.Reflection;
using Coravel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Services;
using LoreAI.Infrastructure.Classification;
using LoreAI.Infrastructure.Content;
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
    builder.Services.AddSingleton<ICycleRunRepository, CycleRunRepository>();
    builder.Services.AddSingleton<ILibraryItemRepository, LibraryItemRepository>();
    builder.Services.AddSingleton<ILibraryIndexStateRepository, LibraryIndexStateRepository>();
    builder.Services.AddSingleton<IToolRepository, ToolRepository>();
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

    builder.Services.AddHttpClient<IRaindropClient, RaindropClient>()
        .AddStandardResilienceHandler();

    // Seul l'appel LLM sort des valeurs par défaut, cf. ClassifierResilience.
    builder.Services.AddHttpClient<IClassifier, AnthropicClassifier>()
        .AddStandardResilienceHandler(ClassifierResilience.Configure);

    // Même cible API et même résilience que le classifieur (S4, lot 5) : appel Anthropic texte libre, pas
    // de tool-use.
    builder.Services.AddHttpClient<IThemeNarrativeGenerator, AnthropicThemeNarrativeGenerator>()
        .AddStandardResilienceHandler(ClassifierResilience.Configure);

    // Politesse envers des sites tiers inconnus (S1, lot 4) : timeout court, pas de retry agressif.
    builder.Services.AddHttpClient<IContentFetcher, HttpContentFetcher>()
        .AddStandardResilienceHandler(ContentFetchResilience.Configure);

    builder.Services.AddHttpClient<IImmediateNotifier, DiscordNotifier>()
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<ICycleReportNotifier, DiscordCycleReportNotifier>()
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<IReportNotifier, DiscordReportNotifier>()
        .AddStandardResilienceHandler();

    // Relance L4 (lot 6), déclenchée par ReconciliationJob.
    builder.Services.AddHttpClient<IReminderNotifier, DiscordReminderNotifier>()
        .AddStandardResilienceHandler();

    builder.Services.AddScheduler();
    builder.Services.AddTransient<UnsortedClassificationJob>();
    builder.Services.AddTransient<LibraryIndexingJob>();
    builder.Services.AddTransient<WeeklyInsightsJob>();
    builder.Services.AddTransient<MonthlyReviewJob>();
    builder.Services.AddTransient<ReconciliationJob>();

    var host = builder.Build();

    // Sonde Docker (#35) : un second processus .NET, sans jamais atteindre host.Run() ni la validation des
    // options non liées (Raindrop/Classifier/Discord peuvent être absentes d'une sonde). Voir HealthCheckMode.
    if (args.Contains("--health-check"))
    {
        Environment.ExitCode = await LoreAI.Worker.HealthCheckMode.RunAsync(host.Services, CancellationToken.None) ? 0 : 1;
        return;
    }

    // O5 (#75, lot 6) : force un passage hors cadence pour les deux jobs à cron lent (hebdo/mensuel),
    // sans attendre leur prochain déclenchement naturel — même patron que --health-check, avant
    // host.Run() et sans valider les options non liées. Les deux jobs ne lèvent jamais (philosophie
    // « jamais throw, toujours logger » déjà en place) : pas de code de sortie dédié à calculer ici.
    if (args.Contains("--run-weekly-insights"))
    {
        await host.Services.GetRequiredService<WeeklyInsightsJob>().Invoke();
        return;
    }

    if (args.Contains("--run-monthly-review"))
    {
        await host.Services.GetRequiredService<MonthlyReviewJob>().Invoke();
        return;
    }

    // #65 : seul signal fiable, en dehors des logs applicatifs, pour savoir quelle version tourne réellement
    // sur mcm8 sans avoir à interroger Docker — utile quand un déploiement partiel laisse un conteneur en retard.
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    Log.Information("LoreAI.Worker {Version} démarré, environnement {Environment}.", version, builder.Environment.EnvironmentName);

    var workerOptions = host.Services.GetRequiredService<IOptions<WorkerOptions>>().Value;
    host.Services.UseScheduler(scheduler =>
    {
        scheduler.Schedule<UnsortedClassificationJob>()
            .Cron(workerOptions.PollingCronExpression)
            .RunOnceAtStart()
            .PreventOverlapping(nameof(UnsortedClassificationJob));

        var libraryIndexSchedule = scheduler.Schedule<LibraryIndexingJob>()
            .Cron(workerOptions.LibraryIndexCronExpression)
            .PreventOverlapping(nameof(LibraryIndexingJob));
        if (workerOptions.IndexLibraryOnStartup)
        {
            libraryIndexSchedule.RunOnceAtStart();
        }

        scheduler.Schedule<WeeklyInsightsJob>()
            .Cron(workerOptions.WeeklyInsightsCronExpression)
            .PreventOverlapping(nameof(WeeklyInsightsJob));

        scheduler.Schedule<MonthlyReviewJob>()
            .Cron(workerOptions.MonthlyReviewCronExpression)
            .PreventOverlapping(nameof(MonthlyReviewJob));

        scheduler.Schedule<ReconciliationJob>()
            .Cron(workerOptions.ReconciliationCronExpression)
            .PreventOverlapping(nameof(ReconciliationJob));
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
