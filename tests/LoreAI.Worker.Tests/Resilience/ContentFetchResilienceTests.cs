using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using LoreAI.Worker.Resilience;

namespace LoreAI.Worker.Tests.Resilience;

/// <summary>Même esprit que <c>ClassifierResilienceTests</c> : exerce le validateur intégré du handler.</summary>
public class ContentFetchResilienceTests
{
    [Fact]
    public void Configure_ProducesAValidHandlerConfiguration()
    {
        var exception = Record.Exception(() => CreateClientWith(ContentFetchResilience.Configure));

        Assert.Null(exception);
    }

    [Fact]
    public void Timeouts_AreShortEnoughToStayPolite()
    {
        // Un site tiers lent ou en panne ne doit pas retarder le cycle : l'article retombe sur son excerpt.
        Assert.True(ContentFetchResilience.AttemptTimeout <= TimeSpan.FromSeconds(10));
        Assert.True(ContentFetchResilience.TotalRequestTimeout > ContentFetchResilience.AttemptTimeout);
    }

    [Fact]
    public void MaxRetryAttempts_IsNotAggressive()
    {
        var options = new HttpStandardResilienceOptions();
        ContentFetchResilience.Configure(options);

        Assert.True(options.Retry.MaxRetryAttempts <= 1);
    }

    /// <summary>Même contrainte que ClassifierResilienceTests : documente le validateur, prouve qu'il est actif.</summary>
    [Fact]
    public void RaisingAttemptTimeoutWithoutWideningTheCircuitBreakerWindow_IsRejected()
    {
        var exception = Record.Exception(() => CreateClientWith(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(40);
            // SamplingDuration laissée à son défaut de 30 s, soit moins du double du timeout par tentative.
        }));

        Assert.IsType<OptionsValidationException>(exception);
    }

    private static HttpClient CreateClientWith(Action<HttpStandardResilienceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("content-fetch").AddStandardResilienceHandler(configure);

        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("content-fetch");
    }
}
