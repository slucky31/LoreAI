using System.Globalization;
using Dapper;
using RaindropAI.Core.Interfaces;
using RaindropAI.Core.Models;

namespace RaindropAI.Infrastructure.Persistence;

public sealed class PollingStateRepository : IPollingStateRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public PollingStateRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<PollingState> GetAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = "SELECT LastRaindropId, LastCreatedUtc, UpdatedAtUtc FROM PollingState WHERE Id = 1";
        var row = await connection.QuerySingleOrDefaultAsync<PollingStateRow>(
            new CommandDefinition(sql, cancellationToken: cancellationToken));

        if (row is null)
        {
            return PollingState.Initial;
        }

        return new PollingState(
            row.LastRaindropId,
            row.LastCreatedUtc is null ? null : DateTimeOffset.Parse(row.LastCreatedUtc, CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(row.UpdatedAtUtc, CultureInfo.InvariantCulture));
    }

    public async Task UpdateAsync(PollingState state, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = """
            INSERT INTO PollingState (Id, LastRaindropId, LastCreatedUtc, UpdatedAtUtc)
            VALUES (1, @LastRaindropId, @LastCreatedUtc, @UpdatedAtUtc)
            ON CONFLICT(Id) DO UPDATE SET
                LastRaindropId = excluded.LastRaindropId,
                LastCreatedUtc = excluded.LastCreatedUtc,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            state.LastRaindropId,
            LastCreatedUtc = state.LastCreatedUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            UpdatedAtUtc = state.UpdatedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        }, cancellationToken: cancellationToken));
    }

    private sealed class PollingStateRow
    {
        public long? LastRaindropId { get; init; }
        public string? LastCreatedUtc { get; init; }
        public required string UpdatedAtUtc { get; init; }
    }
}
