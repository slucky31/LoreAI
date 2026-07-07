using RaindropAI.Core.Enums;
using RaindropAI.Core.Models;
using RaindropAI.Core.Services;

namespace RaindropAI.Core.Tests.Services;

public class DefaultNotificationPolicyTests
{
    [Theory]
    [InlineData(RecommendedAction.ATester, Priority.Haute, true)]
    [InlineData(RecommendedAction.ATester, Priority.Moyenne, false)]
    [InlineData(RecommendedAction.ATester, Priority.Basse, false)]
    [InlineData(RecommendedAction.ALire, Priority.Haute, false)]
    [InlineData(RecommendedAction.Reference, Priority.Haute, false)]
    public void ShouldNotifyImmediately_AppliesDefaultThreshold(RecommendedAction action, Priority priority, bool expected)
    {
        var policy = new DefaultNotificationPolicy();
        var classification = new ClassificationResult(Category.DotNet, action, priority, "raison", "model", "raw");

        var result = policy.ShouldNotifyImmediately(classification);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldNotifyImmediately_HonoursCustomThresholds()
    {
        var policy = new DefaultNotificationPolicy(
            triggerActions: new HashSet<RecommendedAction> { RecommendedAction.ALire },
            minimumPriority: Priority.Moyenne);

        var classification = new ClassificationResult(Category.Formation, RecommendedAction.ALire, Priority.Moyenne, "raison", "model", "raw");

        Assert.True(policy.ShouldNotifyImmediately(classification));
    }
}
