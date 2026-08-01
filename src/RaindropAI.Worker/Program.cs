using Coravel;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Services;
using RaindropAI.Infrastructure.Classification;
using RaindropAI.Infrastructure.Notifications;
using RaindropAI.Infrastructure.Persistence;
using RaindropAI.Infrastructure.Raindrop;
using RaindropAI.Worker.Options;
using RaindropAI.Worker.Resilience;
using RaindropAI.Worker.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, loggerConfiguration) =>
    {
        var logFilePath = builder.Configuration["Logging:FilePath"] ?? "logs/raindropai-.log";
        loggerConfiguration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .WriteTo.Console()
            .WriteTo.File(logFilePath, rollingInterval: Serilog.RollingInterval.Day);
    });

    // Validées au démarrage : une configuration incomplète doit arrêter le service tout de suite,
    // pas produire un worker qui tourne et échoue en silence toutes les 15 minutes.
    builder.Services
        .AddValidatedOptions<SqliteOptions>(builder.Configuration, "Sqlite")
        .AddValidatedOptions<RaindropApiOptions>(builder.Configuration, "Raindrop")
        .AddValidatedOptions<ClassifierOptions>(builder.Configuration, "Classifier")
        .AddValidatedOptions<DiscordOptions>(builder.Configuration, "Discord")
        .AddValidatedOptions<EmailOptions>(builder.Configuration, "Email")
        .AddValidatedOptions<WorkerOptions>(builder.Configuration, "Worker");

    builder.Services.AddSingleton<SqliteConnectionFactory>();
    builder.Services.AddSingleton<IArticleRepository, ArticleRepository>();
    builder.Services.AddSingleton<IPollingStateRepository, PollingStateRepository>();
    builder.Services.AddSingleton<INotificationPolicy, DefaultNotificationPolicy>();
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
            .PreventOverlapping(nameof(UnsortedClassificationJob));

        scheduler.Schedule<DigestNotificationJob>()
            .Cron(workerOptions.DigestCronExpression)
            .PreventOverlapping(nameof(DigestNotificationJob));
    });

    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "RaindropAI.Worker s'est arrêté de façon inattendue.");
}
finally
{
    Log.CloseAndFlush();
}
