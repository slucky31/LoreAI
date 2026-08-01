using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using RaindropAI.Core.Models;
using RaindropAI.Infrastructure.Persistence;

namespace RaindropAI.Infrastructure.Tests.Persistence;

public class PollingStateRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly PollingStateRepository _repository;

    public PollingStateRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"raindropai-test-{Guid.NewGuid():N}.db");
        var factory = new SqliteConnectionFactory(Options.Create(new SqliteOptions { ConnectionString = $"Data Source={_dbPath}" }));
        _repository = new PollingStateRepository(factory);
    }

    [Fact]
    public async Task GetAsync_WithoutPriorState_ReturnsInitial()
    {
        var state = await _repository.GetAsync(CancellationToken.None);

        Assert.Equal(PollingState.Initial, state);
    }

    [Fact]
    public async Task UpdateAsync_ThenGetAsync_RoundTripsValues()
    {
        var expected = new PollingState(123, DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture), DateTimeOffset.Parse("2026-01-02T00:00:00Z", CultureInfo.InvariantCulture));

        await _repository.UpdateAsync(expected, CancellationToken.None);
        var actual = await _repository.GetAsync(CancellationToken.None);

        Assert.Equal(expected.LastRaindropId, actual.LastRaindropId);
        Assert.Equal(expected.LastCreatedUtc, actual.LastCreatedUtc);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdateAsync_CalledTwice_OverwritesSingletonRow()
    {
        await _repository.UpdateAsync(new PollingState(1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
        var second = new PollingState(2, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await _repository.UpdateAsync(second, CancellationToken.None);

        var actual = await _repository.GetAsync(CancellationToken.None);

        Assert.Equal(2, actual.LastRaindropId);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
