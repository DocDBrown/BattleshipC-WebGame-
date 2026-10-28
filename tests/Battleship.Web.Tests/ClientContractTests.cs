using System.Net;
using System.Text.Json;
using Battleship.Shared;
using Battleship.Web.Net;
using Battleship.Web.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Battleship.Web.Tests;

[Collection("real-server")]
public sealed class ClientContractTests
{
    private readonly RealServerFixture _fixture;

    public ClientContractTests(RealServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreateRoomResponse_MatchesServerCamelCaseContract()
    {
        using var http = _fixture.Server.CreateClient();
        using var response = await http.PostAsync("api/rooms", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("roomCode", out var roomCode));
        Assert.False(document.RootElement.TryGetProperty("RoomCode", out _));
        var code = roomCode.GetString();
        Assert.True(ProtocolInfo.IsValidRoomCode(code));
        Assert.Equal($"/api/rooms/{code}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task GetRoomResponse_MatchesServerContract()
    {
        using var http = _fixture.Server.CreateClient();
        using var created = await http.PostAsync("api/rooms", null);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var code = createdBody.RootElement.GetProperty("roomCode").GetString()!;

        using var response = await http.GetAsync($"api/rooms/{code}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("participants", out var participants));
        Assert.Equal(JsonValueKind.Number, participants.ValueKind);
        Assert.Equal(0, participants.GetInt32());
        Assert.False(document.RootElement.TryGetProperty("Participants", out _));
    }

    [Fact]
    public async Task ProblemDetail_MatchesServerContract()
    {
        using var http = _fixture.Server.CreateClient();
        using var response = await http.GetAsync("api/rooms/ZZZZZZ");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("about:blank", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("Not Found", document.RootElement.GetProperty("title").GetString());
        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("No room", document.RootElement.GetProperty("detail").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WsUrl_IncludesRoleQueryAndRoomCode()
    {
        await using var context = await MultiplayerContext.CreateAsync(true, runInitialize: false);
        TestComponent.Set(context.Page, "_roomCode", "ABC234");

        await TestComponent.CallAsync(context.Page, "OpenSocketAsync", "host");

        var uri = Assert.Single(context.Transport.ConnectUris);
        Assert.Equal("/ws/rooms/ABC234", uri.AbsolutePath);
        Assert.Equal("?role=host", uri.Query);
        await context.Page.DisposeAsync();
    }

    [Fact]
    public async Task WsUrl_RoomCodeUppercased()
    {
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            runInitialize: true,
            roomCode: "abc234",
            transport: new FakeWsTransport { AutoHello = true });

        var uri = Assert.Single(context.Transport.ConnectUris);
        Assert.Equal("/ws/rooms/ABC234", uri.AbsolutePath);
        Assert.Equal("ABC234", TestComponent.Get(context.Page, "_roomCode"));
    }

    [Fact]
    public async Task WsUrl_UsesWssWhenPageIsHttps()
    {
        await using var context = await MultiplayerContext.CreateAsync(
            true,
            runInitialize: false,
            settings: new Dictionary<string, string?> { ["Battleship:HttpTimeoutSeconds"] = "2" });
        TestComponent.Set(context.Page, "_roomCode", "ABC234");

        await TestComponent.CallAsync(context.Page, "OpenSocketAsync", "host");

        var uri = Assert.Single(context.Transport.ConnectUris);
        Assert.Equal(Uri.UriSchemeWss, uri.Scheme);
        await context.Page.DisposeAsync();
    }

    [Fact]
    public void HttpClient_UsesHostBaseAddress()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        var baseAddress = new Uri("https://battleship.example/app/");

        WebComposition.Configure(services, configuration, baseAddress);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(baseAddress, scope.ServiceProvider.GetRequiredService<HttpClient>().BaseAddress);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RoomApiClient>());
    }

    [Fact]
    public async Task AppSettings_ServedWithNoStore()
    {
        using var http = _fixture.Server.CreateClient();
        using var response = await http.GetAsync("appsettings.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var cacheControl = response.Headers.CacheControl;
        Assert.NotNull(cacheControl);
        Assert.True(cacheControl!.NoStore);
        Assert.True(cacheControl.NoCache);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal(600, document.RootElement.GetProperty("Battleship").GetProperty("AiMoveDelayMs").GetInt32());
        Assert.Equal(250, document.RootElement.GetProperty("Battleship").GetProperty("AiDecisionBudgetMs").GetInt32());
    }
}
