using Battleship.Web.Net;
using Battleship.Web.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class CompositionTests
{
    [Fact]
    public void GameOptions_LoadedFromAppSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepoPaths.AppSettings, optional: false)
            .Build();

        var options = GameOptions.FromConfiguration(configuration);

        Assert.Equal(600, options.AiMoveDelayMs);
        Assert.Equal(250, options.AiDecisionBudgetMs);
        Assert.Equal(10, options.HttpTimeoutSeconds);
        Assert.Equal(600, options.JoinTimeoutSeconds);
        Assert.Equal(15, options.HandshakeTimeoutSeconds);
        Assert.Equal(60, options.ReadTimeoutSeconds);
        Assert.Equal(20, options.JoinRetryCount);
        Assert.Equal(500, options.JoinRetryDelayMs);
        Assert.Null(options.AiSeed);
    }

    [Fact]
    public void HttpClient_TimeoutFromHttpTimeoutSeconds()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Battleship:HttpTimeoutSeconds"] = "7"
            })
            .Build();
        var services = new ServiceCollection();

        WebComposition.Configure(services, configuration, new Uri("https://example.test/app/"));

        using var provider = services.BuildServiceProvider();
        var http = provider.GetRequiredService<HttpClient>();
        Assert.Equal(TimeSpan.FromSeconds(7), http.Timeout);
        Assert.Equal(new Uri("https://example.test/app/"), http.BaseAddress);
    }

    [Fact]
    public async Task DependencyInjection_WsTransportIsTransient()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        WebComposition.Configure(services, configuration, new Uri("https://example.test/"));

        await using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<WsTransport>();
        var second = provider.GetRequiredService<WsTransport>();
        Assert.NotSame(first, second);
    }

    [Fact]
    public void DependencyInjection_RoomApiClientResolves()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        WebComposition.Configure(services, configuration, new Uri("https://example.test/base/"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var rooms = scope.ServiceProvider.GetRequiredService<RoomApiClient>();
        var http = scope.ServiceProvider.GetRequiredService<HttpClient>();
        Assert.NotNull(rooms);
        Assert.Equal(new Uri("https://example.test/base/"), http.BaseAddress);
    }
}
