using Microsoft.EntityFrameworkCore;

namespace LoreAI.Infrastructure.Persistence;

/// <summary>
/// Applique les migrations EF Core une seule fois par process. Si Postgres est injoignable, l'échec
/// n'est pas mémorisé comme définitif : le drapeau reste baissé, et l'appel suivant (prochain cycle,
/// ou prochaine requête d'un repository) retente — c'est ce qui traite l'indisponibilité de la base
/// comme une panne transitoire plutôt que fatale (ADR 0009 : pas de <c>depends_on</c> vers une instance
/// que LoreAI ne possède pas).
/// </summary>
public sealed class PostgresSchemaGuard(IDbContextFactory<LoreAiDbContext> contextFactory) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _migrated;

    public async Task EnsureMigratedAsync(CancellationToken cancellationToken)
    {
        if (_migrated)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_migrated)
            {
                return;
            }

            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);

            _migrated = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Libère le verrou d'initialisation. Le garde est un singleton, donc disposé à l'arrêt de l'hôte.</summary>
    public void Dispose() => _lock.Dispose();
}
