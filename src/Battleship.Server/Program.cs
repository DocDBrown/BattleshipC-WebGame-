using Battleship.Server.Api;
using Battleship.Server.Background;
using Battleship.Server.Hosting;
using Battleship.Server.Options;
using Battleship.Server.RateLimiting;
using Battleship.Server.Realtime;
using Battleship.Server.Rooms;
using Battleship.Shared;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<BattleshipOptions>()
    .Bind(builder.Configuration.GetSection(BattleshipOptions.SectionName));

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/wasm",
        "application/octet-stream"
    });
});

builder.Services.AddSingleton<OptionsProvider>();
builder.Services.AddSingleton<ProtocolCodec>();
builder.Services.AddSingleton<RoomRegistry>();
builder.Services.AddSingleton<RoomCodeGenerator>();
builder.Services.AddSingleton<IpRateLimiter>();
builder.Services.AddSingleton<MessageRelay>();
builder.Services.AddSingleton<RoomSocketHandler>();
builder.Services.AddSingleton<RoomApi>();
builder.Services.AddHostedService<RoomReaper>();

var app = builder.Build();
var startupOptions = app.Services.GetRequiredService<OptionsProvider>().Get();

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseResponseCompression();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(startupOptions.KeepAliveSeconds)
});

app.UseBlazorFrameworkFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = StaticContent.Types,
    OnPrepareResponse = StaticContent.Prepare
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers["Cache-Control"] = "no-store";
        var options = context.RequestServices.GetRequiredService<OptionsProvider>().Get();
        var isCreate = HttpMethods.IsPost(context.Request.Method);
        var permits = isCreate
            ? options.CreateRoomPermitsPerMinute
            : options.ConnectPermitsPerMinute;
        var limiter = context.RequestServices.GetRequiredService<IpRateLimiter>();
        if (!limiter.TryAcquire(context, isCreate ? "create" : "connect", permits))
        {
            await HttpProblem.WriteAsync(
                context,
                StatusCodes.Status429TooManyRequests,
                "Too Many Requests",
                "The rate limit for this client IP has been exceeded.");
            return;
        }
    }

    await next();
});

app.MapPost("/api/rooms", (HttpContext context) =>
    app.Services.GetRequiredService<RoomApi>().CreateAsync(context));

app.MapGet("/api/rooms/{roomCode}", (HttpContext context) =>
{
    var roomCode = (string?)context.Request.RouteValues["roomCode"] ?? string.Empty;
    return app.Services.GetRequiredService<RoomApi>().GetAsync(context, roomCode);
});

app.MapGet("/ws/rooms/{roomCode}", (HttpContext context) =>
{
    var roomCode = (string?)context.Request.RouteValues["roomCode"] ?? string.Empty;
    return app.Services.GetRequiredService<RoomSocketHandler>().HandleAsync(context, roomCode);
});

app.MapFallback(async (HttpContext context) =>
{
    if (context.Request.Path.StartsWithSegments("/api")
        || context.Request.Path.StartsWithSegments("/ws"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await StaticContent.WriteIndexAsync(context, app.Environment);
});

app.Run();
