using System.Text.Json;
using Battleship.Server.Hosting;
using Battleship.Server.Options;
using Battleship.Server.Rooms;
using Battleship.Shared;

namespace Battleship.Server.Api;

public sealed class RoomApi
{
    private readonly RoomRegistry _registry;
    private readonly RoomCodeGenerator _codes;
    private readonly OptionsProvider _options;
    private readonly ILogger<RoomApi> _logger;

    public RoomApi(
        RoomRegistry registry,
        RoomCodeGenerator codes,
        OptionsProvider options,
        ILogger<RoomApi> logger)
    {
        _registry = registry;
        _codes = codes;
        _options = options;
        _logger = logger;
    }

    public async Task CreateAsync(HttpContext context)
    {
        var options = _options.Get();
        if (_registry.Count >= options.MaxRooms)
        {
            await HttpProblem.WriteAsync(
                context,
                StatusCodes.Status409Conflict,
                "Conflict",
                "The maximum number of live rooms has been reached.");
            return;
        }

        for (var attempt = 0; attempt < options.RoomCodeAttempts; attempt++)
        {
            var code = _codes.Generate();
            var room = new Room(code, DateTimeOffset.UtcNow);
            if (!_registry.TryAdd(room))
            {
                continue;
            }

            _logger.LogInformation("room_created room={RoomCode}", code);
            context.Response.StatusCode = StatusCodes.Status201Created;
            context.Response.Headers.Location = $"/api/rooms/{code}";
            await context.Response.WriteAsJsonAsync(new { roomCode = code });
            return;
        }

        _logger.LogWarning("room_created_failed reason=no_code_available");
        await HttpProblem.WriteAsync(
            context,
            StatusCodes.Status409Conflict,
            "Conflict",
            "No room code is currently available.");
    }

    public async Task GetAsync(HttpContext context, string roomCode)
    {
        var code = (roomCode ?? string.Empty).ToUpperInvariant();
        if (!ProtocolInfo.IsValidRoomCode(code))
        {
            await HttpProblem.WriteAsync(
                context,
                StatusCodes.Status400BadRequest,
                "Validation Failed",
                "The room code must be 6 characters from A-H, J-N, P-Z and 2-9.");
            return;
        }

        var room = _registry.Get(code);
        if (room is null)
        {
            await HttpProblem.WriteAsync(
                context,
                StatusCodes.Status404NotFound,
                "Not Found",
                "No room with that code exists.");
            return;
        }

        await context.Response.WriteAsJsonAsync(new { participants = room.ParticipantCount });
    }
}
