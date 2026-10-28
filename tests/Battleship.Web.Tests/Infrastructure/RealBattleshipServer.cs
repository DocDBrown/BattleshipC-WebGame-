using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Battleship.Shared;
using Battleship.Web.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class RealBattleshipServer : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _log = new();

    private RealBattleshipServer(Process process, Uri baseAddress)
    {
        _process = process;
        BaseAddress = baseAddress;
    }

    public Uri BaseAddress { get; }

    public Uri WsUri(string roomCode, string role) =>
        new($"ws://{BaseAddress.Authority}/ws/rooms/{roomCode}?role={role}");

    public HttpClient CreateClient() => new()
    {
        BaseAddress = BaseAddress,
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static async Task<RealBattleshipServer> StartAsync(Dictionary<string, string?>? settings = null)
    {
        var port = FreePort();
        var project = Path.Combine(RepoPaths.Root, "src", "Battleship.Server", "Battleship.Server.csproj");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepoPaths.Root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add(project);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("--no-build");
        start.ArgumentList.Add("--no-restore");
        start.ArgumentList.Add("--no-launch-profile");
        start.ArgumentList.Add("--");
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["Battleship__CreateRoomPermitsPerMinute"] = "100000";
        start.Environment["Battleship__ConnectPermitsPerMinute"] = "100000";
        start.Environment["Battleship__MessagesPerSecond"] = "10000";
        start.Environment["Battleship__MessageBurst"] = "10000";
        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                start.Environment[$"Battleship__{key}"] = value;
            }
        }

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var server = new RealBattleshipServer(process, new Uri($"http://127.0.0.1:{port}/"));
        process.OutputDataReceived += (_, args) => server.Append(args.Data);
        process.ErrorDataReceived += (_, args) => server.Append(args.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await server.WaitUntilListeningAsync(TimeSpan.FromSeconds(60));
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
        }
    }

    private async Task WaitUntilListeningAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"The test server exited early.{Environment.NewLine}{Log()}");
            }

            try
            {
                using var client = new TcpClient();
                using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
                await client.ConnectAsync(IPAddress.Loopback, BaseAddress.Port, source.Token);
                return;
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                await Task.Delay(100);
            }
        }

        throw new TimeoutException($"The test server did not start.{Environment.NewLine}{Log()}");
    }

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_log)
        {
            _log.AppendLine(line);
        }
    }

    private string Log()
    {
        lock (_log)
        {
            return _log.ToString();
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

internal sealed class RecordingWsTransport : WsTransport
{
    private readonly object _sync = new();

    public List<string> SentTexts { get; } = new();

    public List<(int Code, string? Reason)> Closes { get; } = new();

    public List<Uri> ConnectUris { get; } = new();

    public override Task ConnectAsync(Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ConnectUris.Add(uri);
        }

        return base.ConnectAsync(uri, timeout, cancellationToken);
    }

    public override Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            SentTexts.Add(text);
        }

        return base.SendTextAsync(text, cancellationToken);
    }

    public override Task CloseAsync(int closeCode, string? reason, TimeSpan? timeout = null)
    {
        lock (_sync)
        {
            Closes.Add((closeCode, reason));
        }

        return base.CloseAsync(closeCode, reason, timeout);
    }
}

internal sealed class RealTestEnvironment : IDisposable
{
    private readonly ServiceProvider _provider;

    public RealTestEnvironment(Uri baseAddress, IDictionary<string, string?>? settings = null, RecordingWsTransport? transport = null)
    {
        Navigation = new TestNavigationManager(baseAddress.ToString());
        Js = new TestJsRuntime();
        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();
        HttpClient = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(10) };
        Rooms = new RoomApiClient(HttpClient);
        Transport = transport ?? new RecordingWsTransport();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration);
        services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(Navigation);
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime>(Js);
        services.AddSingleton(Rooms);
        services.AddSingleton<WsTransport>(Transport);
        _provider = services.BuildServiceProvider();
    }

    public TestNavigationManager Navigation { get; }

    public TestJsRuntime Js { get; }

    public IConfiguration Configuration { get; }

    public HttpClient HttpClient { get; }

    public RoomApiClient Rooms { get; }

    public RecordingWsTransport Transport { get; }

    public IServiceProvider Services => _provider;

    public void Dispose()
    {
        HttpClient.Dispose();
        _provider.Dispose();
    }
}

internal sealed class RealMultiplayerContext : IAsyncDisposable
{
    private RealMultiplayerContext(RealTestEnvironment environment, MultiplayerGameProbe page, ComponentHarness harness)
    {
        Environment = environment;
        Page = page;
        Harness = harness;
    }

    public RealTestEnvironment Environment { get; }

    public MultiplayerGameProbe Page { get; }

    public ComponentHarness Harness { get; }

    public RecordingWsTransport Transport => Environment.Transport;

    public string Html => Harness.Html;

    public string? RoomCode => (string?)TestComponent.Get(Page, "_roomCode");

    public string Phase => TestComponent.Get(Page, "_phase")!.ToString()!;

    public string? ErrorCode => (string?)TestComponent.Get(Page, "_errorCode");

    public string? ErrorDetail => (string?)TestComponent.Get(Page, "_errorDetail");

    public string? Notice => (string?)TestComponent.Get(Page, "_notice");

    public Battleship.Web.Session.MultiplayerSession Session =>
        (Battleship.Web.Session.MultiplayerSession)TestComponent.Get(Page, "_session")!;

    public static async Task<RealMultiplayerContext> CreateAsync(
        RealBattleshipServer server,
        bool isHost,
        string? roomCode = null,
        Dictionary<string, string?>? settings = null)
    {
        var values = Defaults(settings);
        var environment = new RealTestEnvironment(server.BaseAddress, values);
        var page = new MultiplayerGameProbe
        {
            IsHost = isHost,
            RoomCode = roomCode,
            RunInitialize = false
        };
        var harness = await ComponentHarness.MountAsync(page, environment.Services);
        TestComponent.Set(page, "_options", GameOptions.FromConfiguration(environment.Configuration));
        return new RealMultiplayerContext(environment, page, harness);
    }

    public Task StartAsync() => TestComponent.CallAsync(Page, Page.IsHost ? "StartHostAsync" : "StartClientAsync");

    public Task ReadyAsync() => TestComponent.CallAsync(Page, "ReadyAsync");

    public Task FireAsync(Battleship.Web.Game.Coordinate coordinate) =>
        TestComponent.CallAsync(Page, "FireAsync", coordinate);

    public Task SendChatAsync(string text) => TestComponent.CallAsync(Page, "SendChatAsync", text);

    public void PlaceFleet((ShipKind Kind, Battleship.Web.Game.Coordinate Start, bool Horizontal)[] layout)
    {
        foreach (var (kind, start, horizontal) in layout)
        {
            Assert.True(Session.OwnBoard.TryPlace(kind, start, horizontal));
        }
    }

    public void Refresh() => Harness.Refresh();

    public async ValueTask DisposeAsync()
    {
        await Page.DisposeAsync();
        await Environment.Transport.DisposeAsync();
        Harness.Dispose();
        Environment.Dispose();
    }

    private static Dictionary<string, string?> Defaults(Dictionary<string, string?>? settings)
    {
        var values = new Dictionary<string, string?>
        {
            ["Battleship:HttpTimeoutSeconds"] = "10",
            ["Battleship:JoinTimeoutSeconds"] = "10",
            ["Battleship:HandshakeTimeoutSeconds"] = "10",
            ["Battleship:ReadTimeoutSeconds"] = "10",
            ["Battleship:JoinRetryCount"] = "10",
            ["Battleship:JoinRetryDelayMs"] = "50"
        };
        if (settings is not null)
        {
            foreach (var pair in settings)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }
}

internal static class RealGameDriver
{
    public static readonly (ShipKind Kind, Battleship.Web.Game.Coordinate Start, bool Horizontal)[] HostLayout =
    {
        (ShipKind.CARRIER, new Battleship.Web.Game.Coordinate(0, 0), true),
        (ShipKind.BATTLESHIP, new Battleship.Web.Game.Coordinate(1, 0), true),
        (ShipKind.CRUISER, new Battleship.Web.Game.Coordinate(2, 0), true),
        (ShipKind.SUBMARINE, new Battleship.Web.Game.Coordinate(3, 0), true),
        (ShipKind.PATROL_BOAT, new Battleship.Web.Game.Coordinate(4, 0), true)
    };

    public static readonly (ShipKind Kind, Battleship.Web.Game.Coordinate Start, bool Horizontal)[] ClientLayout =
    {
        (ShipKind.CARRIER, new Battleship.Web.Game.Coordinate(5, 0), true),
        (ShipKind.BATTLESHIP, new Battleship.Web.Game.Coordinate(6, 0), true),
        (ShipKind.CRUISER, new Battleship.Web.Game.Coordinate(7, 0), true),
        (ShipKind.SUBMARINE, new Battleship.Web.Game.Coordinate(8, 0), true),
        (ShipKind.PATROL_BOAT, new Battleship.Web.Game.Coordinate(9, 0), true)
    };

    public static async Task<(RealMultiplayerContext Host, RealMultiplayerContext Client)> ConnectAsync(
        RealBattleshipServer server,
        Dictionary<string, string?>? settings = null)
    {
        var host = await RealMultiplayerContext.CreateAsync(server, isHost: true, settings: settings);
        var hostStart = host.StartAsync();
        await WaitUntilAsync(
            () => host.RoomCode is not null && host.Phase == "WaitingForPeer",
            "The host did not open the room.");

        var client = await RealMultiplayerContext.CreateAsync(server, isHost: false, host.RoomCode, settings);
        await client.StartAsync();
        await hostStart;
        await WaitUntilAsync(
            () => host.Phase == "Placement" && client.Phase == "Placement",
            "The handshake did not complete.");
        return (host, client);
    }

    public static async Task PlayAsync(RealMultiplayerContext host, RealMultiplayerContext client)
    {
        host.PlaceFleet(HostLayout);
        client.PlaceFleet(ClientLayout);
        await Task.WhenAll(host.ReadyAsync(), client.ReadyAsync());
        await WaitUntilAsync(
            () => host.Phase == "Combat" && client.Phase == "Combat",
            "Combat did not begin.");

        var hostShots = ClientLayout.SelectMany(ship => Cells(ship.Start, ship.Kind)).ToList();
        var clientShots = HostLayout.SelectMany(ship => Cells(ship.Start, ship.Kind)).ToList();
        var hostIndex = 0;
        var clientIndex = 0;
        while (host.Phase != "GameOver" && client.Phase != "GameOver")
        {
            if (host.Session.Turn == Battleship.Web.Session.Side.Self && host.Session.PendingSeq is null)
            {
                Assert.True(hostIndex < hostShots.Count);
                await host.FireAsync(hostShots[hostIndex++]);
                await WaitUntilAsync(
                    () => host.Session.PendingSeq is null
                          && (client.Session.Turn == Battleship.Web.Session.Side.Self || client.Phase == "GameOver"),
                    "The host shot was not resolved.");
                continue;
            }

            if (client.Session.Turn == Battleship.Web.Session.Side.Self && client.Session.PendingSeq is null)
            {
                Assert.True(clientIndex < clientShots.Count);
                await client.FireAsync(clientShots[clientIndex++]);
                await WaitUntilAsync(
                    () => client.Session.PendingSeq is null
                          && (host.Session.Turn == Battleship.Web.Session.Side.Self || host.Phase == "GameOver"),
                    "The client shot was not resolved.");
                continue;
            }

            await Task.Delay(10);
        }

        await WaitUntilAsync(
            () => host.Phase == "GameOver" && client.Phase == "GameOver",
            "Both sessions did not reach game over.");
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string message, int timeoutMs = 30000)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(15);
        }

        throw new TimeoutException(message);
    }

    public static async Task WaitForHtmlAsync(RealMultiplayerContext context, string text, string message)
    {
        await WaitUntilAsync(
            () =>
            {
                try
                {
                    context.Refresh();
                    return context.Html.Contains(text, StringComparison.Ordinal);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
                {
                    return false;
                }
            },
            message,
            10000);
    }

    private static IEnumerable<Battleship.Web.Game.Coordinate> Cells(
        Battleship.Web.Game.Coordinate start,
        ShipKind kind)
    {
        var size = Battleship.Web.Game.Fleet.SizeOf(kind);
        for (var offset = 0; offset < size; offset++)
        {
            yield return new Battleship.Web.Game.Coordinate(start.Row, start.Col + offset);
        }
    }
}

public sealed class RealServerFixture : IAsyncLifetime
{
    internal RealBattleshipServer Server { get; private set; } = null!;

    public Task InitializeAsync() => StartAsync();

    private async Task StartAsync() => Server = await RealBattleshipServer.StartAsync();

    public async Task DisposeAsync() => await Server.DisposeAsync();
}

[CollectionDefinition("real-server")]
public sealed class RealServerCollection : ICollectionFixture<RealServerFixture>
{
}
