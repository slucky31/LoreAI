using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LoreAI.Core.Interfaces;
using LoreAI.Infrastructure.Persistence;
using LoreAI.Mcp.Options;
using LoreAI.Mcp.Security;
using LoreAI.Mcp.Tools;
using ModelContextProtocol.AspNetCore;
using Serilog;

// Culture invariante sur les sinks, comme LoreAI.Worker : des logs rendus à l'identique quelle que soit
// la machine, et alignés sur le conteneur, qui tourne de toute façon en mode globalization-invariant.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, loggerConfiguration) =>
    {
        var logFilePath = builder.Configuration["Serilog:FilePath"] ?? "logs/loreai-mcp-.log";
        loggerConfiguration
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day, formatProvider: CultureInfo.InvariantCulture);
    });

    // Validées au démarrage : une configuration incomplète doit arrêter le service tout de suite, pas
    // laisser un serveur MCP tourner et échouer en silence à chaque appel d'outil.
    builder.Services
        .AddValidatedOptions<PostgresOptions>(builder.Configuration, "Postgres")
        .AddValidatedOptions<McpOptions>(builder.Configuration, "Mcp");

    // Ce process reçoit toujours la chaîne de connexion du rôle loreai_ro (GRANT SELECT, ADR 0009/0014),
    // jamais celle du rôle propriétaire utilisée par LoreAI.Worker — deux conteneurs, deux chaînes
    // distinctes dans docker-compose.yml. Pas de PostgresSchemaGuard/PostgresSchemaInitializer ici :
    // loreai_ro n'a pas les privilèges de migration, et ce n'est pas son rôle — c'est celui du Worker.
    builder.Services.AddDbContextFactory<LoreAiDbContext>((serviceProvider, options) =>
    {
        var postgresOptions = serviceProvider.GetRequiredService<IOptions<PostgresOptions>>().Value;
        options.UseNpgsql(postgresOptions.ConnectionString);
    });
    builder.Services.AddSingleton<ICorpusQueryRepository, CorpusQueryRepository>();

    // Mode sans état (recommandé quand aucune requête serveur→client — sampling, elicitation — n'est
    // utilisée, ADR 0014) : pas d'affinité de session, compatible avec un client qui ne renvoie pas
    // Mcp-Session-Id.
    builder.Services.AddMcpServer()
        .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless)
        .WithTools<CorpusTools>();

    var app = builder.Build();

    // #65 : même besoin que LoreAI.Worker — savoir quelle version tourne réellement sur mcm8 sans avoir
    // à interroger Docker.
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    Log.Information("LoreAI.Mcp {Version} démarré, environnement {Environment}.", version, builder.Environment.EnvironmentName);

    // Défense en profondeur derrière le réseau Tailscale (ADR 0010/0014) : toute requête MCP sans le bon
    // jeton est rejetée avant d'atteindre le protocole.
    app.UseMiddleware<BearerTokenMiddleware>();
    // Chemin explicite (pas la racine, par défaut du SDK) : c'est celui documenté dans le .mcp.json du
    // lot 3 (roadmap) et dans l'ADR 0014.
    app.MapMcp("/mcp");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "LoreAI.Mcp s'est arrêté de façon inattendue.");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}
