using Battleship.Shared;
using Battleship.Web.Game;
using Battleship.Web.Tests.Infrastructure;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class CoordinateTests
{
    [Fact]
    public void AllCells_Has100UniqueCellsRowMajor()
    {
        Assert.Equal(100, Coordinate.AllCells.Count);
        Assert.Equal(100, Coordinate.AllCells.Distinct().Count());
        Assert.Equal(new Coordinate(0, 0), Coordinate.AllCells[0]);
        Assert.Equal(new Coordinate(1, 1), Coordinate.AllCells[11]);
        Assert.Equal(new Coordinate(9, 9), Coordinate.AllCells[99]);
    }

    [Fact]
    public void IsValid_EdgesAndOutside()
    {
        Assert.True(new Coordinate(0, 0).IsValid);
        Assert.True(new Coordinate(9, 9).IsValid);
        Assert.False(new Coordinate(-1, 0).IsValid);
        Assert.False(new Coordinate(0, -1).IsValid);
        Assert.False(new Coordinate(10, 0).IsValid);
        Assert.False(new Coordinate(0, 10).IsValid);
    }

    [Fact]
    public void Label_AndToString_UseA1Format()
    {
        Assert.Equal("A1", new Coordinate(0, 0).Label);
        Assert.Equal("J10", new Coordinate(9, 9).Label);
        Assert.Equal("C4", new Coordinate(2, 3).ToString());
    }

    [Fact]
    public void Offset_AddsDeltas()
    {
        Assert.Equal(new Coordinate(3, 4), new Coordinate(2, 2).Offset(1, 2));
        Assert.Equal(new Coordinate(0, 0), new Coordinate(2, 2).Offset(-2, -2));
    }

    [Fact]
    public void TryParse_DelegatesToBoardCoords()
    {
        Assert.True(Coordinate.TryParse("C7", out var parsed));
        Assert.Equal(new Coordinate(2, 6), parsed);
        Assert.True(Coordinate.TryParse("j10", out var last));
        Assert.Equal(new Coordinate(9, 9), last);
        Assert.False(Coordinate.TryParse("K1", out _));
        Assert.Equal(BoardCoords.TryParse("B2", out _, out _), Coordinate.TryParse("B2", out _));
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        var left = new Coordinate(4, 5);
        var right = new Coordinate(4, 5);
        Assert.Equal(left, right);
        Assert.True(left == right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, new Coordinate(5, 4));
    }
}

public sealed class FleetTests
{
    [Fact]
    public void Ships_FiveStandardShipsTotalling17Cells()
    {
        Assert.Equal(5, Fleet.Ships.Count);
        Assert.Equal(17, Fleet.Ships.Sum(ship => ship.Size));
    }

    [Fact]
    public void Ships_KindsUnique()
    {
        Assert.Equal(Fleet.Ships.Count, Fleet.Ships.Select(ship => ship.Kind).Distinct().Count());
    }

    [Fact]
    public void Get_EachKind_ReturnsDefinition()
    {
        foreach (var definition in Fleet.Ships)
        {
            Assert.Same(definition, Fleet.Get(definition.Kind));
        }
    }

    [Fact]
    public void Get_UndefinedKind_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Fleet.Get((ShipKind)99));
    }

    [Fact]
    public void SizeOfAndNameOf_MatchDefinitions()
    {
        Assert.Equal(5, Fleet.SizeOf(ShipKind.CARRIER));
        Assert.Equal("Patrol Boat", Fleet.NameOf(ShipKind.PATROL_BOAT));
        Assert.Equal("Submarine", Fleet.NameOf(ShipKind.SUBMARINE));
    }
}

public sealed class ShipTests
{
    private static Ship CreateShip()
    {
        var definition = Fleet.Get(ShipKind.CRUISER);
        var cells = new[] { new Coordinate(0, 0), new Coordinate(0, 1), new Coordinate(0, 2) };
        return new Ship(definition, cells, horizontal: true);
    }

    [Fact]
    public void Properties_ComeFromDefinition()
    {
        var ship = CreateShip();
        Assert.Equal(ShipKind.CRUISER, ship.Kind);
        Assert.Equal("Cruiser", ship.Name);
        Assert.Equal(3, ship.Size);
        Assert.True(ship.Horizontal);
        Assert.Equal(3, ship.Cells.Count);
    }

    [Fact]
    public void IsSunk_AllCellsHit_True()
    {
        var ship = CreateShip();
        foreach (var cell in ship.Cells)
        {
            ship.Hits.Add(cell);
        }

        Assert.True(ship.IsSunk);
    }

    [Fact]
    public void IsSunk_PartialHits_False()
    {
        var ship = CreateShip();
        ship.Hits.Add(ship.Cells[0]);
        Assert.False(ship.IsSunk);
    }

    [Fact]
    public void IsSunk_ForeignCoordinateHits_DoNotSink()
    {
        var ship = CreateShip();
        ship.Hits.Add(new Coordinate(5, 5));
        ship.Hits.Add(new Coordinate(6, 6));
        ship.Hits.Add(new Coordinate(7, 7));
        Assert.False(ship.IsSunk);
    }
}

public sealed class BoardTests
{
    [Fact]
    public void TryPlace_Horizontal_Succeeds()
    {
        var board = new Board();
        Assert.True(board.TryPlace(ShipKind.CARRIER, new Coordinate(0, 0), horizontal: true));
        Assert.Equal(5, board.Ships.Single().Cells.Count);
        Assert.Equal(new Coordinate(0, 4), board.Ships.Single().Cells[^1]);
    }

    [Fact]
    public void TryPlace_Vertical_Succeeds()
    {
        var board = new Board();
        Assert.True(board.TryPlace(ShipKind.BATTLESHIP, new Coordinate(2, 3), horizontal: false));
        Assert.Equal(new Coordinate(5, 3), board.Ships.Single().Cells[^1]);
    }

    [Fact]
    public void TryPlace_OutOfBounds_FailsWithNoPartialState()
    {
        var board = new Board();
        Assert.False(board.TryPlace(ShipKind.CARRIER, new Coordinate(0, 6), horizontal: true));
        Assert.Empty(board.Ships);
        Assert.False(board.IsPlaced(ShipKind.CARRIER));
    }

    [Fact]
    public void TryPlace_Overlap_Fails()
    {
        var board = new Board();
        Assert.True(board.TryPlace(ShipKind.CARRIER, new Coordinate(0, 0), horizontal: true));
        Assert.False(board.TryPlace(ShipKind.BATTLESHIP, new Coordinate(0, 3), horizontal: true));
        Assert.Single(board.Ships);
    }

    [Fact]
    public void TryPlace_DuplicateKind_Fails()
    {
        var board = new Board();
        Assert.True(board.TryPlace(ShipKind.PATROL_BOAT, new Coordinate(0, 0), horizontal: true));
        Assert.False(board.TryPlace(ShipKind.PATROL_BOAT, new Coordinate(5, 5), horizontal: false));
        Assert.Single(board.Ships);
    }

    [Fact]
    public void TryPlace_InvalidStart_Fails()
    {
        var board = new Board();
        Assert.False(board.TryPlace(ShipKind.CARRIER, new Coordinate(-1, 0), horizontal: true));
        Assert.Empty(board.Ships);
    }

    [Fact]
    public void TryPlace_AdjacentNonOverlappingShips_Allowed()
    {
        var board = new Board();
        Assert.True(board.TryPlace(ShipKind.CARRIER, new Coordinate(0, 0), horizontal: true));
        Assert.True(board.TryPlace(ShipKind.BATTLESHIP, new Coordinate(1, 0), horizontal: true));
        Assert.Equal(2, board.Ships.Count);
    }

    [Fact]
    public void ShipAt_InvalidCoordinate_ReturnsNull()
    {
        var board = TestBoards.Fleet();
        Assert.Null(board.ShipAt(new Coordinate(-1, 0)));
        Assert.Null(board.ShipAt(new Coordinate(10, 10)));
    }

    [Fact]
    public void AllShipsPlaced_OnlyWhenFleetComplete()
    {
        var board = new Board();
        Assert.False(board.AllShipsPlaced);
        foreach (var (kind, start, horizontal) in TestBoards.Layout.Take(4))
        {
            Assert.True(board.TryPlace(kind, start, horizontal));
            Assert.False(board.AllShipsPlaced);
        }

        Assert.True(board.TryPlace(TestBoards.Layout[4].Kind, TestBoards.Layout[4].Start, true));
        Assert.True(board.AllShipsPlaced);
    }

    [Fact]
    public void AllSunk_RequiresFullFleet()
    {
        var board = new Board();
        Assert.False(board.AllSunk);
        board = TestBoards.Fleet();
        foreach (var ship in board.Ships.Take(4))
        {
            foreach (var cell in ship.Cells)
            {
                ship.Hits.Add(cell);
            }
        }

        Assert.False(board.AllSunk);
        foreach (var cell in board.Ships[^1].Cells)
        {
            board.Ships[^1].Hits.Add(cell);
        }

        Assert.True(board.AllSunk);
    }

    [Fact]
    public void RecordShot_TryGetShot_IsTried()
    {
        var board = new Board();
        var coordinate = new Coordinate(3, 4);
        Assert.False(board.IsTried(coordinate));
        board.RecordShot(coordinate, ShotOutcome.HIT);
        Assert.True(board.IsTried(coordinate));
        Assert.True(board.TryGetShot(coordinate, out var outcome));
        Assert.Equal(ShotOutcome.HIT, outcome);
    }

    [Fact]
    public void Clear_ResetsShipsAndShots()
    {
        var board = TestBoards.Fleet();
        board.RecordShot(new Coordinate(9, 9), ShotOutcome.MISS);
        board.Clear();
        Assert.Empty(board.Ships);
        Assert.Empty(board.Shots);
        Assert.False(board.AllShipsPlaced);
    }

    [Fact]
    public void TryAutoPlaceAll_PlacesFullFleetInBoundsNonOverlapping()
    {
        var board = new Board();
        Assert.True(board.TryAutoPlaceAll(new Random(1234)));
        Assert.Equal(5, board.Ships.Count);
        var allCells = board.Ships.SelectMany(ship => ship.Cells).ToArray();
        Assert.Equal(17, allCells.Length);
        Assert.Equal(17, allCells.Distinct().Count());
        Assert.All(allCells, cell => Assert.True(cell.IsValid));
    }

    [Fact]
    public void TryAutoPlaceAll_SameSeed_SameLayout()
    {
        var first = new Board();
        var second = new Board();
        Assert.True(first.TryAutoPlaceAll(new Random(777)));
        Assert.True(second.TryAutoPlaceAll(new Random(777)));
        Assert.Equal(Signature(first), Signature(second));
    }

    [Fact]
    public void TryAutoPlaceAll_ManySeeds_LayoutsVary()
    {
        var layouts = new HashSet<string>(StringComparer.Ordinal);
        for (var seed = 0; seed < 20; seed++)
        {
            var board = new Board();
            Assert.True(board.TryAutoPlaceAll(new Random(seed)));
            layouts.Add(Signature(board));
        }

        Assert.True(layouts.Count > 1);
    }

    private static string Signature(Board board) => string.Join(
        ";",
        board.Ships.Select(ship => $"{ship.Kind}:{string.Join(",", ship.Cells)}"));
}

public sealed class ShotResolverTests
{
    [Fact]
    public void Resolve_EmptyCell_Miss()
    {
        var board = TestBoards.Fleet();
        var result = new ShotResolver().Resolve(board, new Coordinate(9, 9));
        Assert.Equal(ShotOutcome.MISS, result.Outcome);
        Assert.Null(result.Ship);
    }

    [Fact]
    public void Resolve_ShipCell_Hit()
    {
        var board = TestBoards.Fleet();
        var result = new ShotResolver().Resolve(board, new Coordinate(0, 0));
        Assert.Equal(ShotOutcome.HIT, result.Outcome);
    }

    [Fact]
    public void Resolve_Hit_DoesNotRevealShipKind()
    {
        var board = TestBoards.Fleet();
        var result = new ShotResolver().Resolve(board, new Coordinate(0, 0));
        Assert.Null(result.Ship);
    }

    [Fact]
    public void Resolve_FinalCellOfShip_SunkWithKind()
    {
        var board = TestBoards.Fleet();
        var resolver = new ShotResolver();
        resolver.Resolve(board, new Coordinate(4, 0));
        var result = resolver.Resolve(board, new Coordinate(4, 1));
        Assert.Equal(ShotOutcome.SUNK, result.Outcome);
        Assert.Equal(ShipKind.PATROL_BOAT, result.Ship);
    }

    [Fact]
    public void Resolve_FinalShipOfFleet_GameOver()
    {
        var board = TestBoards.Fleet();
        foreach (var kind in new[] { ShipKind.CARRIER, ShipKind.BATTLESHIP, ShipKind.CRUISER, ShipKind.SUBMARINE })
        {
            TestBoards.Sink(board, kind);
        }

        var resolver = new ShotResolver();
        resolver.Resolve(board, new Coordinate(4, 0));
        var result = resolver.Resolve(board, new Coordinate(4, 1));
        Assert.Equal(ShotOutcome.GAME_OVER, result.Outcome);
        Assert.Equal(ShipKind.PATROL_BOAT, result.Ship);
    }

    [Fact]
    public void Resolve_RepeatCoordinate_Throws()
    {
        var board = TestBoards.Fleet();
        var resolver = new ShotResolver();
        resolver.Resolve(board, new Coordinate(9, 9));
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(board, new Coordinate(9, 9)));
    }

    [Fact]
    public void Resolve_InvalidCoordinate_Throws()
    {
        var board = TestBoards.Fleet();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShotResolver().Resolve(board, new Coordinate(-1, 0)));
    }

    [Fact]
    public void Resolve_RecordsShotOnBoard()
    {
        var board = TestBoards.Fleet();
        var resolver = new ShotResolver();
        resolver.Resolve(board, new Coordinate(0, 0));
        resolver.Resolve(board, new Coordinate(9, 9));
        Assert.True(board.TryGetShot(new Coordinate(0, 0), out var hit));
        Assert.Equal(ShotOutcome.HIT, hit);
        Assert.True(board.TryGetShot(new Coordinate(9, 9), out var miss));
        Assert.Equal(ShotOutcome.MISS, miss);
    }
}

public sealed class TargetBoardTests
{
    [Fact]
    public void Record_MarksTried()
    {
        var target = new TargetBoard();
        Assert.False(target.IsTried(new Coordinate(1, 2)));
        target.Record(new Coordinate(1, 2), ShotOutcome.MISS);
        Assert.True(target.IsTried(new Coordinate(1, 2)));
    }

    [Fact]
    public void MarkSunkLine_Horizontal_MarksSegment()
    {
        var target = new TargetBoard();
        target.Record(new Coordinate(0, 0), ShotOutcome.HIT);
        target.Record(new Coordinate(0, 1), ShotOutcome.HIT);
        target.Record(new Coordinate(0, 2), ShotOutcome.SUNK);
        target.MarkSunkLine(new Coordinate(0, 1), 3);
        Assert.True(target.IsSunk(new Coordinate(0, 0)));
        Assert.True(target.IsSunk(new Coordinate(0, 1)));
        Assert.True(target.IsSunk(new Coordinate(0, 2)));
    }

    [Fact]
    public void MarkSunkLine_Vertical_MarksSegment()
    {
        var target = new TargetBoard();
        target.Record(new Coordinate(2, 5), ShotOutcome.HIT);
        target.Record(new Coordinate(3, 5), ShotOutcome.SUNK);
        target.MarkSunkLine(new Coordinate(3, 5), 2);
        Assert.True(target.IsSunk(new Coordinate(2, 5)));
        Assert.True(target.IsSunk(new Coordinate(3, 5)));
    }

    [Fact]
    public void MarkSunkLine_AtBoardEdge_MarksSegment()
    {
        var target = new TargetBoard();
        target.Record(new Coordinate(9, 7), ShotOutcome.HIT);
        target.Record(new Coordinate(9, 8), ShotOutcome.HIT);
        target.Record(new Coordinate(9, 9), ShotOutcome.SUNK);
        target.MarkSunkLine(new Coordinate(9, 9), 3);
        Assert.True(target.IsSunk(new Coordinate(9, 7)));
        Assert.True(target.IsSunk(new Coordinate(9, 8)));
        Assert.True(target.IsSunk(new Coordinate(9, 9)));
    }

    [Fact]
    public void MarkSunkLine_Ambiguous_MarksOnlyCoordinate()
    {
        var target = new TargetBoard();
        target.Record(new Coordinate(0, 0), ShotOutcome.HIT);
        target.Record(new Coordinate(0, 2), ShotOutcome.HIT);
        target.MarkSunkLine(new Coordinate(0, 0), 3);
        Assert.True(target.IsSunk(new Coordinate(0, 0)));
        Assert.False(target.IsSunk(new Coordinate(0, 2)));
    }

    [Fact]
    public void MarkSunkLine_NeverIncludesMisses()
    {
        var target = new TargetBoard();
        target.Record(new Coordinate(0, 0), ShotOutcome.HIT);
        target.Record(new Coordinate(0, 1), ShotOutcome.MISS);
        target.Record(new Coordinate(0, 2), ShotOutcome.SUNK);
        target.MarkSunkLine(new Coordinate(0, 1), 3);
        Assert.True(target.IsSunk(new Coordinate(0, 1)));
        Assert.False(target.IsSunk(new Coordinate(0, 0)));
        Assert.False(target.IsSunk(new Coordinate(0, 2)));
    }

    [Fact]
    public void MarkSunk_ExplicitCells()
    {
        var target = new TargetBoard();
        target.MarkSunk(new[] { new Coordinate(1, 1), new Coordinate(1, 2) });
        Assert.True(target.IsSunk(new Coordinate(1, 1)));
        Assert.True(target.IsSunk(new Coordinate(1, 2)));
        Assert.False(target.IsSunk(new Coordinate(1, 3)));
    }

    [Fact]
    public void Clear_ResetsShotsAndSunk()
    {
        var target = new TargetBoard();
        target.Record(new Coordinate(0, 0), ShotOutcome.HIT);
        target.MarkSunk(new[] { new Coordinate(0, 0) });
        target.Clear();
        Assert.Empty(target.Shots);
        Assert.False(target.IsSunk(new Coordinate(0, 0)));
        Assert.False(target.IsTried(new Coordinate(0, 0)));
    }
}

public sealed class BoardViewTests
{
    [Fact]
    public void Own_ReportsEmptyShipHitMissSunk()
    {
        var board = TestBoards.Fleet();
        var resolver = new ShotResolver();
        resolver.Resolve(board, new Coordinate(9, 9));
        resolver.Resolve(board, new Coordinate(0, 0));
        resolver.Resolve(board, new Coordinate(4, 0));
        resolver.Resolve(board, new Coordinate(4, 1));
        var view = BoardView.Own(board).ToDictionary(cell => cell.Coordinate, cell => cell.State);
        Assert.Equal(CellState.Empty, view[new Coordinate(8, 8)]);
        Assert.Equal(CellState.Ship, view[new Coordinate(1, 3)]);
        Assert.Equal(CellState.Hit, view[new Coordinate(0, 0)]);
        Assert.Equal(CellState.Miss, view[new Coordinate(9, 9)]);
        Assert.Equal(CellState.Sunk, view[new Coordinate(4, 0)]);
    }

    [Fact]
    public void Placement_ShowsOnlyShipOrEmpty()
    {
        var board = TestBoards.Fleet();
        var states = BoardView.Placement(board).Select(cell => cell.State).Distinct().ToArray();
        Assert.All(states, state => Assert.True(state is CellState.Ship or CellState.Empty));
    }

    [Fact]
    public void Target_ReportsUnknownMissHitSunk()
    {
        var target = new TargetBoard();
        target.Record(new Coordinate(0, 0), ShotOutcome.MISS);
        target.Record(new Coordinate(0, 1), ShotOutcome.HIT);
        target.Record(new Coordinate(0, 2), ShotOutcome.SUNK);
        target.MarkSunk(new[] { new Coordinate(0, 2) });
        var view = BoardView.Target(target).ToDictionary(cell => cell.Coordinate, cell => cell.State);
        Assert.Equal(CellState.Unknown, view[new Coordinate(5, 5)]);
        Assert.Equal(CellState.Miss, view[new Coordinate(0, 0)]);
        Assert.Equal(CellState.Hit, view[new Coordinate(0, 1)]);
        Assert.Equal(CellState.Sunk, view[new Coordinate(0, 2)]);
    }

    [Fact]
    public void Target_NeverReportsShipState()
    {
        var target = new TargetBoard();
        target.Record(new Coordinate(0, 0), ShotOutcome.HIT);
        Assert.DoesNotContain(BoardView.Target(target), cell => cell.State == CellState.Ship);
    }

    [Fact]
    public void AllViews_Return100CellsRowMajor()
    {
        var board = TestBoards.Fleet();
        var target = new TargetBoard();
        foreach (var view in new[] { BoardView.Own(board), BoardView.Placement(board), BoardView.Target(target) })
        {
            Assert.Equal(100, view.Count);
            for (var index = 0; index < 100; index++)
            {
                Assert.Equal(Coordinate.AllCells[index], view[index].Coordinate);
            }
        }
    }
}

public sealed class ShotResultTests
{
    [Fact]
    public void IsGameOver_OnlyForGameOverOutcome()
    {
        Assert.True(new ShotResult(ShotOutcome.GAME_OVER, ShipKind.CARRIER, new Coordinate(0, 0)).IsGameOver);
        Assert.False(new ShotResult(ShotOutcome.SUNK, ShipKind.CARRIER, new Coordinate(0, 0)).IsGameOver);
        Assert.False(new ShotResult(ShotOutcome.HIT, null, new Coordinate(0, 0)).IsGameOver);
        Assert.False(new ShotResult(ShotOutcome.MISS, null, new Coordinate(0, 0)).IsGameOver);
    }
}
