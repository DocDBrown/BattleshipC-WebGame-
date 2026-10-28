using System.Net;
using System.Net.Http;
using Battleship.Web.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class TestEnvironment : IDisposable
{
    private readonly ServiceProvider _provider;

    public TestEnvironment(
        IDictionary<string, string?>? settings = null,
        StubHttpMessageHandler? handler = null,
        FakeWsTransport? transport = null,
        string baseUri = "https://example.test/")
    {
        Navigation = new TestNavigationManager(baseUri);
        Js = new TestJsRuntime();
        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();
        Handler = handler ?? StubHttpMessageHandler.Status(HttpStatusCode.NotFound);
        Transport = transport ?? new FakeWsTransport();
        HttpClient = new HttpClient(Handler, disposeHandler: false)
        {
            BaseAddress = new Uri(baseUri)
        };
        Rooms = new RoomApiClient(HttpClient);

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddSingleton<NavigationManager>(Navigation);
        services.AddSingleton<IJSRuntime>(Js);
        services.AddSingleton(Rooms);
        services.AddSingleton<WsTransport>(Transport);
        _provider = services.BuildServiceProvider();
    }

    public TestNavigationManager Navigation { get; }

    public TestJsRuntime Js { get; }

    public IConfiguration Configuration { get; }

    public StubHttpMessageHandler Handler { get; }

    public FakeWsTransport Transport { get; }

    public HttpClient HttpClient { get; }

    public RoomApiClient Rooms { get; }

    public IServiceProvider Services => _provider;

    public void Dispose()
    {
        HttpClient.Dispose();
        _provider.Dispose();
    }
}
