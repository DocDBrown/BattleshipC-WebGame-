using System.Text.RegularExpressions;
using Battleship.Shared;
using Battleship.Web.Components;
using Battleship.Web.Game;
using Battleship.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class CrossBrowserUiTests
{
    [Fact]
    public async Task SinglePlayerGame_ChromeEdgeFirefoxSafari()
    {
        await using var context = await SinglePlayerContext.CreateAsync();
        context.StartBattle();
        AssertPortableMarkup(context.Html);

        var index = await File.ReadAllTextAsync(RepoPaths.Web("wwwroot", "index.html"));
        Assert.StartsWith("<!DOCTYPE html>", index, StringComparison.Ordinal);
        Assert.Contains("lang=\"en\"", index, StringComparison.Ordinal);
        Assert.Contains("name=\"viewport\"", index, StringComparison.Ordinal);
        Assert.DoesNotContain("<!--[if", index, StringComparison.Ordinal);

        var script = await File.ReadAllTextAsync(RepoPaths.AppJs);
        Assert.DoesNotContain("window.chrome", script, StringComparison.Ordinal);
        Assert.DoesNotContain("webkit", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("moz", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("msie", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("document.getElementById", script, StringComparison.Ordinal);
        Assert.Contains("navigator.clipboard", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiplayerGame_ChromeEdgeFirefoxSafari()
    {
        await using var context = await MultiplayerContext.CreateAsync(
            false,
            runInitialize: true,
            roomCode: "abc234",
            transport: new FakeWsTransport { AutoHello = true });
        context.Harness.Refresh();

        AssertPortableMarkup(context.Html);
        Assert.Equal(Uri.UriSchemeWss, Assert.Single(context.Transport.ConnectUris).Scheme);
        Assert.Contains("role=\"grid\"", context.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("webkit", context.Html, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertPortableMarkup(string html)
    {
        Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
        Assert.False(Regex.IsMatch(html, @"\son[a-z]+\s*=", RegexOptions.IgnoreCase));
        UiAssert.AssertStandardButtonMarkup(html);
        Assert.Contains("role=\"grid\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"", html, StringComparison.Ordinal);
    }
}

public sealed class AccessibilityUiTests
{
    [Fact]
    public async Task AxeScan_NoViolations()
    {
        using var environment = new TestEnvironment();
        var home = new Battleship.Web.Pages.Index();
        using var homeHarness = await ComponentHarness.MountAsync(home, environment.Services);
        CheckPage(homeHarness.Html, expectBoard: false);

        await using var game = await SinglePlayerContext.CreateAsync();
        game.StartBattle();
        CheckPage(game.Html, expectBoard: true);

        var index = await File.ReadAllTextAsync(RepoPaths.Web("wwwroot", "index.html"));
        Assert.Contains("lang=\"en\"", index, StringComparison.Ordinal);
        Assert.Contains("name=\"viewport\"", index, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cells_HaveAriaLabelsWithState()
    {
        using var environment = new TestEnvironment();
        var cells = new List<CellView>();
        for (var row = 0; row < BoardCoords.Size; row++)
        {
            for (var col = 0; col < BoardCoords.Size; col++)
            {
                var state = (CellState)(((row * BoardCoords.Size) + col) % 6);
                cells.Add(new CellView(new Coordinate(row, col), state));
            }
        }

        var grid = new BoardGrid { Cells = cells, Interactive = false, Label = "Accessibility board" };
        using var harness = await ComponentHarness.MountAsync(grid, environment.Services);
        var html = harness.Html;

        Assert.Contains("role=\"grid\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Accessibility board\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-rowcount=\"10\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-colcount=\"10\"", html, StringComparison.Ordinal);
        Assert.Equal(100, UiAssert.Count(html, "role=\"gridcell\""));
        UiAssert.AssertCellMarkup(html);
        Assert.Contains("A1 unknown", html, StringComparison.Ordinal);
        Assert.Contains("empty water", html, StringComparison.Ordinal);
        Assert.Contains("your ship", html, StringComparison.Ordinal);
        Assert.Contains("sunk ship", html, StringComparison.Ordinal);
        Assert.Contains(" hit", html, StringComparison.Ordinal);
        Assert.Contains(" miss", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FocusMovesToExpectedElementAfterActions()
    {
        using var environment = new TestEnvironment();
        var board = new Board();
        var cells = BoardView.Placement(board);
        var grid = new BoardGrid { Cells = cells, Interactive = true, Label = "Focus board" };
        using var harness = await ComponentHarness.MountAsync(grid, environment.Services);

        await TestComponent.CallAsync(grid, "HandleKeyDown", new KeyboardEventArgs { Key = "ArrowDown" });
        await TestComponent.CallAsync(grid, "HandleKeyDown", new KeyboardEventArgs { Key = "ArrowRight" });
        await TestComponent.CallAsync(grid, "HandleKeyDown", new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.EndsWith("-1-2", (string)environment.Js.Calls[^1].Arguments[0]!, StringComparison.Ordinal);
        Assert.Equal(new Coordinate(1, 2), TestComponent.Get(grid, "Cursor"));

        await TestComponent.CallAsync(grid, "ActivateAsync", cells[(2 * BoardCoords.Size) + 3]);
        await TestComponent.CallAsync(grid, "HandleKeyDown", new KeyboardEventArgs { Key = "ArrowDown" });
        Assert.EndsWith("-3-3", (string)environment.Js.Calls[^1].Arguments[0]!, StringComparison.Ordinal);
        Assert.Equal(new Coordinate(3, 3), TestComponent.Get(grid, "Cursor"));
    }

    [Fact]
    public async Task MobileViewport_NoHorizontalScroll()
    {
        var index = await File.ReadAllTextAsync(RepoPaths.Web("wwwroot", "index.html"));
        Assert.Contains("name=\"viewport\" content=\"width=device-width, initial-scale=1.0\"", index, StringComparison.Ordinal);

        var css = await File.ReadAllTextAsync(RepoPaths.Web("wwwroot", "css", "app.css"));
        Assert.Contains("box-sizing: border-box", css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width: 760px)", css, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: 1fr;", css, StringComparison.Ordinal);
        Assert.Contains("minmax(0", css, StringComparison.Ordinal);
        Assert.DoesNotContain("100vw", css, StringComparison.Ordinal);
    }

    private static void CheckPage(string html, bool expectBoard)
    {
        UiAssert.AssertStandardButtonMarkup(html);
        UiAssert.AssertLabelledInputs(html);
        Assert.Equal(1, UiAssert.Count(html, "<h1>"));
        foreach (Match match in Regex.Matches(html, "<img\\b[^>]*>", RegexOptions.CultureInvariant))
        {
            Assert.Contains("alt=", match.Value, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("href=\"\"", html, StringComparison.Ordinal);
        if (!expectBoard)
        {
            Assert.Contains("for=\"room-code\"", html, StringComparison.Ordinal);
            return;
        }

        Assert.Contains("role=\"grid\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Your board\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Enemy targeting board\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-rowcount=\"10\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-colcount=\"10\"", html, StringComparison.Ordinal);
        UiAssert.AssertCellMarkup(html);
    }
}
