using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using LoreAI.Core.Enums;
using LoreAI.Core.Interfaces;
using LoreAI.Core.Models;
using LoreAI.Worker;
using LoreAI.Worker.Options;

// LoreAI.Worker.Options masque Microsoft.Extensions.Options dans ce fichier.
using MsOptions = Microsoft.Extensions.Options.Options;

namespace LoreAI.Worker.Tests.Services;

public class HealthCheckModeTests
{
    [Fact]
    public async Task RunAsync_RecentOkRun_ReturnsTrue()
    {
        var services = BuildServices(
            [new CycleRun(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(-1), CycleOutcome.Ok, 1, 1, 0, 0, 0, null)],
            healthMaxCycleAgeMinutes: 45);

        var healthy = await HealthCheckMode.RunAsync(services, TestContext.Current.CancellationToken);

        Assert.True(healthy);
    }

    [Fact]
    public async Task RunAsync_NoRuns_ReturnsFalse()
    {
        var services = BuildServices([], healthMaxCycleAgeMinutes: 45);

        var healthy = await HealthCheckMode.RunAsync(services, TestContext.Current.CancellationToken);

        Assert.False(healthy);
    }

    [Fact]
    public async Task RunAsync_StaleLastRun_ReturnsFalse()
    {
        var services = BuildServices(
            [new CycleRun(DateTimeOffset.UtcNow.AddMinutes(-90), DateTimeOffset.UtcNow.AddMinutes(-90), CycleOutcome.Ok, 1, 1, 0, 0, 0, null)],
            healthMaxCycleAgeMinutes: 45);

        var healthy = await HealthCheckMode.RunAsync(services, TestContext.Current.CancellationToken);

        Assert.False(healthy);
    }

    [Fact]
    public async Task RunAsync_UsesConfiguredMaxCycleAge()
    {
        var runAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        var services = BuildServices(
            [new CycleRun(runAt, runAt, CycleOutcome.Ok, 1, 1, 0, 0, 0, null)],
            healthMaxCycleAgeMinutes: 10);

        var healthy = await HealthCheckMode.RunAsync(services, TestContext.Current.CancellationToken);

        Assert.False(healthy);
    }

    [Fact]
    public async Task RunAsync_RequestsExactlyThreeRecentRuns()
    {
        var cycleRunRepository = Substitute.For<ICycleRunRepository>();
        cycleRunRepository.GetRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        var services = new ServiceCollection()
            .AddSingleton(cycleRunRepository)
            .AddSingleton(MsOptions.Create(new WorkerOptions { HealthMaxCycleAgeMinutes = 45 }))
            .BuildServiceProvider();

        await HealthCheckMode.RunAsync(services, TestContext.Current.CancellationToken);

        await cycleRunRepository.Received(1).GetRecentAsync(3, Arg.Any<CancellationToken>());
    }

    private static ServiceProvider BuildServices(IReadOnlyList<CycleRun> recentRuns, int healthMaxCycleAgeMinutes)
    {
        var cycleRunRepository = Substitute.For<ICycleRunRepository>();
        cycleRunRepository.GetRecentAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(recentRuns);

        return new ServiceCollection()
            .AddSingleton(cycleRunRepository)
            .AddSingleton(MsOptions.Create(new WorkerOptions { HealthMaxCycleAgeMinutes = healthMaxCycleAgeMinutes }))
            .BuildServiceProvider();
    }
}
