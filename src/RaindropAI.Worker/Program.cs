using Coravel;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Services;
using RaindropAI.Infrastructure.Classification;
using RaindropAI.Infrastructure.Notifications;
using RaindropAI.Infrastructure.Persistence;
using RaindropAI.Infrastructure.Raindrop;
using RaindropAI.Worker.Options;
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

    builder.Services.Configure<SqliteOptions>(builder.Configuration.GetSection("Sqlite"));
    builder.Services.Configure<RaindropApiOptions>(builder.Configuration.GetSection("Raindrop"));
    builder.Services.Configure<ClassifierOptions>(builder.Configuration.GetSection("Classifier"));
    builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));
    builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection("Email"));
    builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));

    builder.Services.AddSingleton<SqliteConnectionFactory>();
    builder.Services.AddSingleton<IArticleRepository, ArticleRepository>();
    builder.Services.AddSingleton<IPollingStateRepository, PollingStateRepository>();
    builder.Services.AddSingleton<INotificationPolicy, DefaultNotificationPolicy>();
    builder.Services.AddSingleton<IDigestNotifier, EmailNotifier>();

    builder.Services.AddHttpClient<IRaindropClient, RaindropClient>()
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<IClassifier, AnthropicClassifier>()
        .AddStandardResilienceHandler();

    builder.Services.AddHttpClient<IImmediateNotifier, DiscordNotifier>()
        .AddStandardResilienceHandler();

    builder.Services.AddScheduler();
    builder.Services.AddTransient<RaindropPollingJob>();
    builder.Services.AddTransient<DigestNotificationJob>();

    var host = builder.Build();

    var workerOptions = host.Services.GetRequiredService<IOptions<WorkerOptions>>().Value;
    host.Services.UseScheduler(scheduler =>
    {
        scheduler.Schedule<RaindropPollingJob>()
            .Cron(workerOptions.PollingCronExpression)
            .PreventOverlapping(nameof(RaindropPollingJob));

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
