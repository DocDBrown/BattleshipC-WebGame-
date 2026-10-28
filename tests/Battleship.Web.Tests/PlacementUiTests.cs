using Battleship.Shared;
using Battleship.Web.Components;
using Battleship.Web.Game;
using Battleship.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class PlacementUiTests
{
    [Fact]
    public async Task DragShipOntoBoard_PlacesShip()
    {
        var board = new Board();
        await using var panel = await MountPanelAsync(board);

        TestComponent.Call(panel.Panel, "OnDragStart", ShipKind.CARRIER);
        await TestComponent.CallAsync(panel.Panel, "DropAt", new Coordinate(0, 0));

        Assert.True(board.IsPlaced(ShipKind.CARRIER));
        Assert.Contains("dropped", (string)TestComponent.Get(panel.Panel, "Message")!, StringComparison.Ordinal);
        Assert.Contains("placed", panel.Harness.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TouchPlacement_PlacesShip()
    {
        var board = new Board();
        await using var panel = await MountPanelAsync(board);

        await TestComponent.CallAsync(panel.Panel, "PlaceAt", new Coordinate(2, 3));

        Assert.True(board.IsPlaced(ShipKind.CARRIER));
        Assert.Equal("Carrier placed at C4.", TestComponent.Get(panel.Panel, "Message"));
    }

    [Fact]
    public async Task RotateShip_ChangesOrientation()
    {
        var board = new Board();
        await using var panel = await MountPanelAsync(board);

        TestComponent.Call(panel.Panel, "Rotate");
        TestComponent.Call(panel.Panel, "StateHasChanged");

        Assert.False((bool)TestComponent.Get(panel.Panel, "Horizontal")!);
        Assert.Equal("Orientation: vertical.", TestComponent.Get(panel.Panel, "Message"));
        Assert.Contains("Vertical", panel.Harness.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidDrop_RejectedWithFeedback()
    {
        var board = new Board();
        await using var panel = await MountPanelAsync(board);

        TestComponent.Call(panel.Panel, "OnDragStart", ShipKind.CARRIER);
        await TestComponent.CallAsync(panel.Panel, "DropAt", new Coordinate(9, 9));

        Assert.False(board.IsPlaced(ShipKind.CARRIER));
        Assert.Contains("outside the board or overlaps", (string)TestComponent.Get(panel.Panel, "Message")!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AutoPlace_PlacesFullFleet()
    {
        var board = new Board();
        await using var panel = await MountPanelAsync(board);
        TestComponent.SetEnum(panel.Panel, "_mode", "Automatic");

        await TestComponent.CallAsync(panel.Panel, "AutoPlace");

        Assert.Equal(5, board.Ships.Count);
        Assert.True(board.AllShipsPlaced);
        Assert.Contains("automatically", (string)TestComponent.Get(panel.Panel, "Message")!, StringComparison.Ordinal);
        Assert.Contains("Auto-deploy fleet", panel.Harness.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clear_RemovesAllShips()
    {
        var board = new Board();
        Assert.True(board.TryAutoPlaceAll(new Random(7)));
        await using var panel = await MountPanelAsync(board);

        await TestComponent.CallAsync(panel.Panel, "Reset");

        Assert.Empty(board.Ships);
        Assert.Equal("Fleet cleared.", TestComponent.Get(panel.Panel, "Message"));
    }

    [Fact]
    public async Task StartDisabledUntilFleetPlaced()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        Assert.True(UiAssert.ElementHasAttribute(context.Html, "Start battle", "disabled"));

        context.PlaceFleet();
        context.Harness.Refresh();

        Assert.False(UiAssert.ElementHasAttribute(context.Html, "Start battle", "disabled"));
    }

    [Fact]
    public async Task KeyboardOnlyPlacement_Works()
    {
        using var environment = new TestEnvironment();
        var board = new Board();
        var cells = BoardView.Placement(board);
        var activated = new List<Coordinate>();
        var rotations = 0;
        var grid = new BoardGrid
        {
            Cells = cells,
            Interactive = true,
            Label = "Keyboard placement"
        };
        grid.OnActivate = EventCallback.Factory.Create<Coordinate>(grid, coordinate => activated.Add(coordinate));
        grid.OnRotate = EventCallback.Factory.Create(grid, () => rotations++);
        using var harness = await ComponentHarness.MountAsync(grid, environment.Services);

        await TestComponent.CallAsync(grid, "HandleKeyDown", new KeyboardEventArgs { Key = "ArrowRight" });
        await TestComponent.CallAsync(grid, "HandleKeyDown", new KeyboardEventArgs { Key = "r" });
        await TestComponent.CallAsync(grid, "ActivateAsync", cells[0]);

        Assert.Single(activated);
        Assert.Equal(new Coordinate(0, 0), activated[0]);
        Assert.Equal(1, rotations);
        var focus = environment.Js.Calls[^1];
        Assert.Equal("battleship.focusById", focus.Identifier);
        Assert.EndsWith("-0-1", (string)focus.Arguments[0]!, StringComparison.Ordinal);

        var panelBoard = new Board();
        await using var panel = await MountPanelAsync(panelBoard);
        await TestComponent.CallAsync(panel.Panel, "PlaceAt", new Coordinate(0, 0));
        Assert.True(panelBoard.IsPlaced(ShipKind.CARRIER));
        Assert.False(UiAssert.ElementHasAttribute(panel.Harness.Html, "data-coordinate=\"A1\"", "disabled"));
    }

    private static async Task<PanelFixture> MountPanelAsync(Board board)
    {
        var environment = new TestEnvironment();
        var panel = new PlacementPanel { Board = board };
        var harness = await ComponentHarness.MountAsync(panel, environment.Services);
        return new PanelFixture(environment, panel, harness);
    }

    private sealed class PanelFixture : IAsyncDisposable
    {
        public PanelFixture(TestEnvironment environment, PlacementPanel panel, ComponentHarness harness)
        {
            Environment = environment;
            Panel = panel;
            Harness = harness;
        }

        public TestEnvironment Environment { get; }

        public PlacementPanel Panel { get; }

        public ComponentHarness Harness { get; }

        public ValueTask DisposeAsync()
        {
            Harness.Dispose();
            Environment.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
