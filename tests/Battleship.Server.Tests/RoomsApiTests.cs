using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net.WebSockets;
using Battleship.Server.RateLimiting;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class RoomsApiTests
{
    private static Dictionary<string, string?> Settings(int? createPermits = null, int? connectPermits = null, int? maxRooms = null)
    {
        var settings = new Dictionary<string, string?>();
        if (createPermits is not null)
        {
            settings["Battleship:CreateRoomPermitsPerMinute"] = createPermits.Value.ToString();
        }

        if (connectPermits is not null)
        {
            settings["Battleship:ConnectPermitsPerMinute"] = connectPermits.Value.ToString();
        }

        if (maxRooms is not null)
        {
            settings["Battleship:MaxRooms"] = maxRooms.Value.ToString();
        }

        return settings;
    }

    [Fact]
    public async Task PostRooms_Returns201WithRoomCodeJson()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("api/rooms", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.True(Battleship.Shared.ProtocolInfo.IsValidRoomCode(body!["roomCode"]));
    }

    [Fact]
    public async Task PostRooms_LocationHeaderResolvesViaGet()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("api/rooms", null);
        var location = response.Headers.Location!.ToString();
        using var get = await client.GetAsync(location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task PostRooms_CacheControlNoStore()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("api/rooms", null);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task PostRooms_With31stRequestPerMinute_Returns429Problem()
    {
        await using var factory = new RealServerFactory(Settings(createPermits: 30));
        using var client = factory.CreateClient();
        for (var i = 0; i < 30; i++)
        {
            using var response = await client.PostAsync("api/rooms", null);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var limited = await client.PostAsync("api/rooms", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task PostRooms_AtCapacity_Returns409Problem()
    {
        await using var factory = new RealServerFactory(Settings(maxRooms: 1));
        using var client = factory.CreateClient();
        using var first = await client.PostAsync("api/rooms", null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var second = await client.PostAsync("api/rooms", null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task PostRooms_WithArbitraryBody_StillCreates()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("api/rooms", new StringContent("{\"anything\":true}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task GetRoom_ReturnsParticipantsZeroOneTwo()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var zero = await client.GetAsync($"api/rooms/{code}");
        Assert.Equal(0, (await zero.Content.ReadFromJsonAsync<Dictionary<string, int>>())!["participants"]);
        using var host = await factory.ConnectAsync(code, "host");
        using var one = await client.GetAsync($"api/rooms/{code}");
        Assert.Equal(1, (await one.Content.ReadFromJsonAsync<Dictionary<string, int>>())!["participants"]);
        using var peer = await factory.ConnectAsync(code, "client");
        using var two = await client.GetAsync($"api/rooms/{code}");
        Assert.Equal(2, (await two.Content.ReadFromJsonAsync<Dictionary<string, int>>())!["participants"]);
        await host.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await peer.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task GetRoom_ResponseContainsOnlyParticipants()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var get = await client.GetAsync($"api/rooms/{code}");
        using var document = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Single(document.RootElement.EnumerateObject());
        Assert.Equal("participants", document.RootElement.EnumerateObject().Single().Name);
    }

    [Fact]
    public async Task GetRoom_LowercaseCode_Accepted()
    {
        await using var factory = new RealServerFactory();
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        using var response = await client.GetAsync($"api/rooms/{code.ToLowerInvariant()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRoom_InvalidCode_Returns400Problem()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("api/rooms/ABC12I");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task GetRoom_OverlongOrEncodedCode_Returns400Problem()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var overlong = await client.GetAsync("api/rooms/ABCDEFG");
        using var encoded = await client.GetAsync("api/rooms/ABC%20234");
        Assert.Equal(HttpStatusCode.BadRequest, overlong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, encoded.StatusCode);
    }

    [Fact]
    public async Task GetRoom_UnknownCode_Returns404Problem()
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("api/rooms/ABC234");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task GetRoom_With121stRequestPerMinute_Returns429Problem()
    {
        await using var factory = new RealServerFactory(Settings(connectPermits: 120));
        var (client, code) = await factory.CreateRoomAsync();
        using var _ = client;
        for (var i = 0; i < 120; i++)
        {
            using var response = await client.GetAsync($"api/rooms/{code}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var limited = await client.GetAsync($"api/rooms/{code}");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task UnsupportedMethods_Return405(string method)
    {
        await using var factory = new RealServerFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "api/rooms");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task ErrorResponses_UseProblemJsonContentType()
    {
        await using var factory = new RealServerFactory(Settings(maxRooms: 1));
        using var client = factory.CreateClient();
        using var invalid = await client.GetAsync("api/rooms/BAD");
        using var missing = await client.GetAsync("api/rooms/ABC234");
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType!.MediaType);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public void RateLimit_PerIpIndependent()
    {
        var limiter = new IpRateLimiter();
        Assert.True(limiter.TryAcquire("create", "10.0.0.1", 1));
        Assert.True(limiter.TryAcquire("create", "10.0.0.2", 1));
        Assert.False(limiter.TryAcquire("create", "10.0.0.1", 1));
        Assert.False(limiter.TryAcquire("create", "10.0.0.2", 1));
    }

    [Fact]
    public async Task RateLimit_XForwardedForNotTrusted()
    {
        await using var factory = new RealServerFactory(Settings(createPermits: 2));
        using var client = factory.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/rooms");
            request.Headers.Add("X-Forwarded-For", $"10.0.0.{i + 1}");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var third = new HttpRequestMessage(HttpMethod.Post, "api/rooms");
        third.Headers.Add("X-Forwarded-For", "10.0.0.99");
        using var limited = await client.SendAsync(third);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
