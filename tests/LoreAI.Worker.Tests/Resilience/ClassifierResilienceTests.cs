using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using LoreAI.Worker.Resilience;

namespace LoreAI.Worker.Tests.Resilience;

/// <summary>
/// Ces tests exercent le validateur intégré du handler de résilience plutôt que de simplement relire
/// les constantes : c'est lui qui arbitre les contraintes entre timeouts et disjoncteur, et lui qui
/// ferait échouer le démarrage du worker.
/// </summary>
public class ClassifierResilienceTests
{
    [Fact]
    public void Configure_ProducesAValidHandlerConfiguration()
    {
        var exception = Record.Exception(() => CreateClientWith(ClassifierResilience.Configure));

        Assert.Null(exception);
    }

    [Fact]
    public void AttemptTimeout_IsGenerousEnoughForAModelGeneration()
    {
        // 10 s (le défaut) annulerait puis rejouerait une génération lente, déjà facturée côté Anthropic.
        Assert.True(ClassifierResilience.AttemptTimeout >= TimeSpan.FromSeconds(30));
        Assert.True(ClassifierResilience.TotalRequestTimeout > ClassifierResilience.AttemptTimeout);
    }

    /// <summary>
    /// Garde-fou : allonger le timeout par tentative sans élargir la fenêtre du disjoncteur fait échouer
    /// le démarrage. Ce test documente la contrainte et prouve que le validateur est bien actif.
    /// </summary>
    [Fact]
    public void RaisingAttemptTimeoutWithoutWideningTheCircuitBreakerWindow_IsRejected()
    {
        var exception = Record.Exception(() => CreateClientWith(options =>
        {
            options.AttemptTimeout.Timeout = ClassifierResilience.AttemptTimeout;
            options.TotalRequestTimeout.Timeout = ClassifierResilience.TotalRequestTimeout;
            // SamplingDuration laissée à son défaut de 30 s, soit moins du double du timeout par tentative.
        }));

        Assert.IsType<OptionsValidationException>(exception);
    }

    /// <summary>
    /// Le retry doit rejouer un 429 ou un 5xx, jamais un 400 : un schéma d'outil invalide ne passera pas
    /// davantage au second essai, et le rejouer ne ferait que facturer des appels pour rien.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task RetryPolicy_HandlesOnlyTransientFailures(HttpStatusCode status, bool expectedRetry)
    {
        var options = new HttpStandardResilienceOptions();
        ClassifierResilience.Configure(options);

        using var response = new HttpResponseMessage(status);
        var context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        try
        {
            var shouldRetry = await options.Retry.ShouldHandle(
                new RetryPredicateArguments<HttpResponseMessage>(context, Outcome.FromResult(response), 0));

            Assert.Equal(expectedRetry, shouldRetry);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }

    private static HttpClient CreateClientWith(Action<HttpStandardResilienceOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("classifier").AddStandardResilienceHandler(configure);

        // La création du client matérialise le pipeline et déclenche la validation des options.
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("classifier");
    }
}
