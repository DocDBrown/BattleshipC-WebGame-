using System.Text.Json;
using Battleship.Server.Api;
using Battleship.Server.Options;
using Battleship.Server.Rooms;
using Battleship.Shared;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Battleship.Server.Tests;

public sealed class RoomApiTests
{
    private static (RoomApi Api, RoomRegistry Registry, TestLogger<RoomApi> Logger) Create(BattleshipOptions? options = null, RoomCodeGenerator? generator = null)
    {
        var registry = new RoomRegistry();
        var logger = new TestLogger<RoomApi>();
        var api = new RoomApi(registry, generator ?? new RoomCodeGenerator(), TestData.Options(options), logger);
        return (api, registry, logger);
    }

    [Fact]
    public async Task Create_AtMaxRooms_Returns409Problem()
    {
        var (api, registry, _) = Create(new BattleshipOptions { MaxRooms = 1 });
        registry.TryAdd(new Room("ABC234", DateTimeOffset.UtcNow));
        var context = TestData.HttpContext();
        await api.CreateAsync(context);
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task Create_Success_Returns201WithLocationAndBody()
    {
        var (api, _, _) = Create();
        var context = TestData.HttpContext();
        await api.CreateAsync(context);
        Assert.Equal(201, context.Response.StatusCode);
        Assert.StartsWith("/api/rooms/", context.Response.Headers.Location.ToString(), StringComparison.Ordinal);
        using var document = JsonDocument.Parse(TestData.Body(context));
        Assert.True(ProtocolInfo.IsValidRoomCode(document.RootElement.GetProperty("roomCode").GetString()));
    }

    [Fact]
    public async Task Create_Success_RoomAwaitingHostWithZeroParticipants()
    {
        var (api, registry, _) = Create();
        var context = TestData.HttpContext();
        await api.CreateAsync(context);
        using var document = JsonDocument.Parse(TestData.Body(context));
        var code = document.RootElement.GetProperty("roomCode").GetString()!;
        var room = registry.Get(code);
        Assert.NotNull(room);
        Assert.Equal(0, room!.ParticipantCount);
    }

    [Fact]
    public async Task Create_Success_LogsRoomCreated()
    {
        var (api, _, logger) = Create();
        await api.CreateAsync(TestData.HttpContext());
        Assert.Contains(logger.Messages, entry => entry.Contains("room_created", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_AllAttemptsCollide_Returns409AndLogsWarning()
    {
        var (api, registry, logger) = Create(new BattleshipOptions { RoomCodeAttempts = 3 }, new FixedGenerator("ABC234"));
        registry.TryAdd(new Room("ABC234", DateTimeOffset.UtcNow));
        var context = TestData.HttpContext();
        await api.CreateAsync(context);
        Assert.Equal(409, context.Response.StatusCode);
        Assert.Contains(logger.Messages, entry => entry.Contains("room_created_failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get_InvalidCode_Returns400Problem()
    {
        var (api, _, _) = Create();
        var context = TestData.HttpContext();
        await api.GetAsync(context, "ABC12I");
        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task Get_LowercaseCode_Normalized()
    {
        var (api, registry, _) = Create();
        registry.TryAdd(new Room("ABC234", DateTimeOffset.UtcNow));
        var context = TestData.HttpContext();
        await api.GetAsync(context, "abc234");
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownCode_Returns404Problem()
    {
        var (api, _, _) = Create();
        var context = TestData.HttpContext();
        await api.GetAsync(context, "ABC234");
        Assert.Equal(404, context.Response.StatusCode);
    }

    [Fact]
    public async Task Get_ExistingRoom_ReturnsParticipantCountOnly()
    {
        var (api, registry, _) = Create();
        var room = new Room("ABC234", DateTimeOffset.UtcNow);
        room.Assign(TestData.Participant(RoomRole.Host));
        room.Assign(TestData.Participant(RoomRole.Client));
        registry.TryAdd(room);
        var context = TestData.HttpContext();
        await api.GetAsync(context, "ABC234");
        using var document = JsonDocument.Parse(TestData.Body(context));
        Assert.Equal(2, document.RootElement.GetProperty("participants").GetInt32());
        Assert.Single(document.RootElement.EnumerateObject());
    }

    private sealed class FixedGenerator : RoomCodeGenerator
    {
        private readonly string _code;
        public FixedGenerator(string code) => _code = code;
        public override string Generate() => _code;
    }
}
