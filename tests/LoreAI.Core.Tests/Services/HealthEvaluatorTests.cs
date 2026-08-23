using LoreAI.Core.Enums;
using LoreAI.Core.Models;
using LoreAI.Core.Services;

namespace LoreAI.Core.Tests.Services;

public class HealthEvaluatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-23T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(45);

    [Fact]
    public void IsHealthy_NoRuns_ReturnsFalse()
    {
        Assert.False(HealthEvaluator.IsHealthy([], Now, MaxAge));
    }

    [Fact]
    public void IsHealthy_RecentOkRun_ReturnsTrue()
    {
        var runs = new[] { CreateRun(Now.AddMinutes(-5), CycleOutcome.Ok) };

        Assert.True(HealthEvaluator.IsHealthy(runs, Now, MaxAge));
    }

    [Fact]
    public void IsHealthy_StaleLastRun_ReturnsFalse()
    {
        var runs = new[] { CreateRun(Now.AddMinutes(-46), CycleOutcome.Ok) };

        Assert.False(HealthEvaluator.IsHealthy(runs, Now, MaxAge));
    }

    [Fact]
    public void IsHealthy_ExactlyAtThreshold_ReturnsTrue()
    {
        var runs = new[] { CreateRun(Now.AddMinutes(-45), CycleOutcome.Ok) };

        Assert.True(HealthEvaluator.IsHealthy(runs, Now, MaxAge));
    }

    [Fact]
    public void IsHealthy_SingleFailedRun_ReturnsTrue()
    {
        // Une panne ponctuelle de l'API Raindrop ne doit pas déclencher l'alarme — il faut 3 échecs de suite.
        var runs = new[] { CreateRun(Now.AddMinutes(-1), CycleOutcome.Failed) };

        Assert.True(HealthEvaluator.IsHealthy(runs, Now, MaxAge));
    }

    [Fact]
    public void IsHealthy_ThreeConsecutiveFailures_ReturnsFalse()
    {
        var runs = new[]
        {
            CreateRun(Now.AddMinutes(-1), CycleOutcome.Failed),
            CreateRun(Now.AddMinutes(-16), CycleOutcome.Failed),
            CreateRun(Now.AddMinutes(-31), CycleOutcome.Failed),
        };

        Assert.False(HealthEvaluator.IsHealthy(runs, Now, MaxAge));
    }

    [Fact]
    public void IsHealthy_TwoFailuresThenAnOk_ReturnsTrue()
    {
        // Le plus récent d'abord (contrat de GetRecentAsync) : un succès récent efface les échecs précédents.
        var runs = new[]
        {
            CreateRun(Now.AddMinutes(-1), CycleOutcome.Ok),
            CreateRun(Now.AddMinutes(-16), CycleOutcome.Failed),
            CreateRun(Now.AddMinutes(-31), CycleOutcome.Failed),
        };

        Assert.True(HealthEvaluator.IsHealthy(runs, Now, MaxAge));
    }

    [Fact]
    public void IsHealthy_EmptyCyclesAreNotFailures_ReturnsTrue()
    {
        var runs = new[]
        {
            CreateRun(Now.AddMinutes(-1), CycleOutcome.Empty),
            CreateRun(Now.AddMinutes(-16), CycleOutcome.Empty),
            CreateRun(Now.AddMinutes(-31), CycleOutcome.Empty),
        };

        Assert.True(HealthEvaluator.IsHealthy(runs, Now, MaxAge));
    }

    private static CycleRun CreateRun(DateTimeOffset completedUtc, CycleOutcome outcome) =>
        new(completedUtc.AddMinutes(-1), completedUtc, outcome, 0, 0, 0, 0, 0, null);
}
