using Microsoft.AspNetCore.Http;
using LoreAI.Mcp.Options;
using LoreAI.Mcp.Security;

// LoreAI.Mcp.Options masque Microsoft.Extensions.Options dans ce fichier (même convention que
// LoreAI.Worker.Tests.Services.HealthCheckModeTests).
using MsOptions = Microsoft.Extensions.Options.Options;

namespace LoreAI.Mcp.Tests.Security;

public class BearerTokenMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_CorrectBearerToken_CallsNext()
    {
        var context = CreateContext(authorizationHeader: "Bearer secret-token");
        var nextCalled = false;
        var middleware = new BearerTokenMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, MsOptions.Create(new McpOptions { BearerToken = "secret-token" }));

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_MissingAuthorizationHeader_ReturnsUnauthorizedAndDoesNotCallNext()
    {
        var context = CreateContext(authorizationHeader: null);
        var nextCalled = false;
        var middleware = new BearerTokenMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, MsOptions.Create(new McpOptions { BearerToken = "secret-token" }));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WrongBearerToken_ReturnsUnauthorized()
    {
        var context = CreateContext(authorizationHeader: "Bearer wrong-token");
        var middleware = new BearerTokenMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, MsOptions.Create(new McpOptions { BearerToken = "secret-token" }));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_HeaderWithoutBearerScheme_ReturnsUnauthorized()
    {
        var context = CreateContext(authorizationHeader: "secret-token");
        var middleware = new BearerTokenMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, MsOptions.Create(new McpOptions { BearerToken = "secret-token" }));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    /// <summary>La longueur du jeton fourni ne doit jamais faire lever d'exception, même très différente du secret.</summary>
    [Fact]
    public async Task InvokeAsync_TokenShorterThanSecret_ReturnsUnauthorizedWithoutThrowing()
    {
        var context = CreateContext(authorizationHeader: "Bearer x");
        var middleware = new BearerTokenMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, MsOptions.Create(new McpOptions { BearerToken = "secret-token" }));

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext(string? authorizationHeader)
    {
        var context = new DefaultHttpContext
        {
            Response = { Body = new MemoryStream() },
        };
        if (authorizationHeader is not null)
        {
            context.Request.Headers.Authorization = authorizationHeader;
        }

        return context;
    }
}
