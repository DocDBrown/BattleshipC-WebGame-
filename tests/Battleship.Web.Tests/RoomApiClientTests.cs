using System.Net;
using Battleship.Web.Net;
using Battleship.Web.Tests.Infrastructure;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class RoomApiClientTests
{
    [Fact]
    public async Task Create_201_ReturnsOkWithCode()
    {
        var (client, _) = Create(StubHttpMessageHandler.Json(HttpStatusCode.Created, "{\"roomCode\":\"ABC234\"}"));
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal("ABC234", result.Value);
    }

    [Fact]
    public async Task Create_201EmptyCode_ReturnsInternal()
    {
        var (client, _) = Create(StubHttpMessageHandler.Json(HttpStatusCode.Created, "{\"roomCode\":\"\"}"));
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("INTERNAL", result.ErrorCode);
    }

    [Fact]
    public async Task Create_201MalformedJson_ReturnsFailureNotThrows()
    {
        var (client, _) = Create(StubHttpMessageHandler.Json(HttpStatusCode.Created, "not-json"));
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("INTERNAL", result.ErrorCode);
    }

    [Fact]
    public async Task Create_400_ReturnsValidationFailed()
    {
        var (client, _) = Create(Problem(HttpStatusCode.BadRequest, "invalid request"));
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.Equal("VALIDATION_FAILED", result.ErrorCode);
        Assert.Equal("invalid request", result.Detail);
    }

    [Fact]
    public async Task Create_409_ReturnsConflict()
    {
        var (client, _) = Create(Problem(HttpStatusCode.Conflict, "room collision"));
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.Equal("CONFLICT", result.ErrorCode);
    }

    [Fact]
    public async Task Create_429_ReturnsRateLimited()
    {
        var (client, _) = Create(Problem(HttpStatusCode.TooManyRequests, "slow down"));
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.Equal("RATE_LIMITED", result.ErrorCode);
    }

    [Fact]
    public async Task Create_500_ReturnsUpstreamUnavailable()
    {
        var (client, _) = Create(Problem(HttpStatusCode.InternalServerError, "boom"));
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.Equal("UPSTREAM_UNAVAILABLE", result.ErrorCode);
    }

    [Fact]
    public async Task Create_Timeout_ReturnsUpstreamUnavailable()
    {
        var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return StubHttpMessageHandler.Response(HttpStatusCode.Created, "{\"roomCode\":\"ABC234\"}");
        });
        var (client, _) = Create(handler, TimeSpan.FromMilliseconds(50));
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.False(result.Success);
        Assert.Equal("UPSTREAM_UNAVAILABLE", result.ErrorCode);
        Assert.Contains("did not respond", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_NetworkFailure_ReturnsUpstreamUnavailable()
    {
        var (client, _) = Create(StubHttpMessageHandler.Throws(new HttpRequestException("offline")));
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.Equal("UPSTREAM_UNAVAILABLE", result.ErrorCode);
        Assert.Contains("could not be reached", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_200_ReturnsParticipants()
    {
        var (client, _) = Create(StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"participants\":2}"));
        var result = await client.GetRoomAsync("ABC234", CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(2, result.Value);
    }

    [Fact]
    public async Task Get_404_ReturnsNotFound()
    {
        var (client, _) = Create(Problem(HttpStatusCode.NotFound, "no room"));
        var result = await client.GetRoomAsync("ABC234", CancellationToken.None);
        Assert.Equal("NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Get_429_ReturnsRateLimited()
    {
        var (client, _) = Create(Problem(HttpStatusCode.TooManyRequests, "slow down"));
        var result = await client.GetRoomAsync("ABC234", CancellationToken.None);
        Assert.Equal("RATE_LIMITED", result.ErrorCode);
    }

    [Fact]
    public async Task Get_EscapesRoomCodeInPath()
    {
        var (client, handler) = Create(StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"participants\":1}"));
        _ = await client.GetRoomAsync("A B/?", CancellationToken.None);
        var uri = Assert.Single(handler.Requests).RequestUri!;
        Assert.Contains("A%20B%2F%3F", uri.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadDetail_ProblemJson_ExtractsDetail()
    {
        var (client, _) = Create(Problem(HttpStatusCode.NotFound, "Room is missing"));
        var result = await client.GetRoomAsync("ABC234", CancellationToken.None);
        Assert.Equal("Room is missing", result.Detail);
    }

    [Fact]
    public async Task ReadDetail_NonJsonBody_FallsBackToReasonPhrase()
    {
        var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(
            StubHttpMessageHandler.Response(HttpStatusCode.InternalServerError, "plain words", "Server exploded")));
        var (client, _) = Create(handler);
        var result = await client.CreateRoomAsync(CancellationToken.None);
        Assert.Equal("Server exploded", result.Detail);
    }

    [Fact]
    public async Task CallerCancellation_DistinguishedFromTimeout()
    {
        var handler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return StubHttpMessageHandler.Response(HttpStatusCode.Created, "{\"roomCode\":\"ABC234\"}");
        });
        var (client, _) = Create(handler, TimeSpan.FromSeconds(30));
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var result = await client.CreateRoomAsync(source.Token);
        Assert.False(result.Success);
        Assert.Equal("UPSTREAM_UNAVAILABLE", result.ErrorCode);
        Assert.Contains("did not respond", result.Detail, StringComparison.Ordinal);
    }

    private static StubHttpMessageHandler Problem(HttpStatusCode status, string detail) =>
        StubHttpMessageHandler.Json(status, $"{{\"title\":\"Problem\",\"detail\":\"{detail}\"}}");

    private static (RoomApiClient Client, StubHttpMessageHandler Handler) Create(
        StubHttpMessageHandler handler,
        TimeSpan? timeout = null)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(10)
        };
        return (new RoomApiClient(http), handler);
    }
}
