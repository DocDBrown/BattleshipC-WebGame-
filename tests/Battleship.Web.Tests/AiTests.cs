using System.Diagnostics;
using System.Reflection;
using Battleship.Shared;
using Battleship.Web.Ai;
using Battleship.Web.Game;
using Xunit;

namespace Battleship.Web.Tests;

public sealed class AiViewTests
{
    [Fact]
    public void Type_HoldsNoReferenceToBoardOrShips()
    {
        var types = typeof(AiView)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(field => field.FieldType)
            .Concat(typeof(AiView).GetProperties().Select(property => property.PropertyType));
        Assert.DoesNotContain(types, type => type == typeof(Board) || type == typeof(Ship) || type == typeof(ShipDefinition));
    }

    [Fact]
    public void RecordShot_Miss_AddedToMisses()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.MISS, null);
        Assert.Contains(new Coordinate(0, 0), view.Misses);
        Assert.Empty(view.Hits);
    }

    [Fact]
    public void RecordShot_Hit_AddedToHits()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(1, 1), ShotOutcome.HIT, null);
        Assert.Contains(new Coordinate(1, 1), view.Hits);
        Assert.Empty(view.Misses);
    }

    [Fact]
    public void RecordShot_Sunk_MovesSegmentToSunkAndRemovesShip()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(0, 1), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(0, 2), ShotOutcome.SUNK, ShipKind.CRUISER);
        Assert.Empty(view.Hits);
        Assert.Equal(3, view.SunkCells.Count);
        Assert.DoesNotContain(ShipKind.CRUISER, view.RemainingShips);
    }

    [Fact]
    public void RecordShot_GameOver_TreatedAsSunk()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(0, 1), ShotOutcome.GAME_OVER, ShipKind.PATROL_BOAT);
        Assert.Contains(new Coordinate(0, 0), view.SunkCells);
        Assert.Contains(new Coordinate(0, 1), view.SunkCells);
        Assert.DoesNotContain(ShipKind.PATROL_BOAT, view.RemainingShips);
    }

    [Fact]
    public void RecordShot_SunkAmbiguous_FallsBackToSingleCell()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.SUNK, ShipKind.PATROL_BOAT);
        Assert.Contains(new Coordinate(0, 0), view.SunkCells);
        Assert.Single(view.SunkCells);
    }

    [Fact]
    public void RecordShot_SunkAdjacentShips_KeepsOtherShipHits()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(1, 1), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.SUNK, ShipKind.PATROL_BOAT);
        Assert.Contains(new Coordinate(1, 1), view.Hits);
        Assert.DoesNotContain(new Coordinate(1, 1), view.SunkCells);
    }

    [Fact]
    public void RecordShot_SunkWithoutShip_KeepsHit()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(2, 2), ShotOutcome.SUNK, null);
        Assert.Contains(new Coordinate(2, 2), view.Hits);
        Assert.Empty(view.SunkCells);
    }

    [Fact]
    public void Untried_ExcludesShots()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.MISS, null);
        view.RecordShot(new Coordinate(9, 9), ShotOutcome.HIT, null);
        var untried = view.Untried();
        Assert.Equal(98, untried.Count);
        Assert.DoesNotContain(new Coordinate(0, 0), untried);
        Assert.DoesNotContain(new Coordinate(9, 9), untried);
    }

    [Fact]
    public void IsUntried_InvalidCoordinate_False()
    {
        var view = new AiView();
        Assert.False(view.IsUntried(new Coordinate(-1, 0)));
        Assert.False(view.IsUntried(new Coordinate(0, 10)));
        Assert.True(view.IsUntried(new Coordinate(0, 0)));
    }
}

public sealed class AiRandomTests
{
    [Fact]
    public void PickUntried_NoneLeft_Throws()
    {
        var view = new AiView();
        foreach (var cell in Coordinate.AllCells)
        {
            view.RecordShot(cell, ShotOutcome.MISS, null);
        }

        Assert.Throws<InvalidOperationException>(() => AiRandom.PickUntried(view, new Random(1)));
    }

    [Fact]
    public void PickUntried_ReturnsOnlyUntried()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.MISS, null);
        var picked = AiRandom.PickUntried(view, new Random(5));
        Assert.True(view.IsUntried(picked));
    }

    [Fact]
    public void Pick_ReturnsMemberOfCandidates()
    {
        var candidates = new List<Coordinate> { new(1, 1), new(2, 2), new(3, 3) };
        var picked = AiRandom.Pick(candidates, new Random(3));
        Assert.Contains(picked, candidates);
    }

    [Fact]
    public void Pick_EmptyCandidates_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AiRandom.Pick(new List<Coordinate>(), new Random(1)));
    }
}

public sealed class AiStrategyContractTests
{
    [Fact]
    public void ChooseShot_AcceptsOnlyAiView()
    {
        var methods = typeof(IAiStrategy).GetMethods();
        var method = Assert.Single(methods);
        Assert.Equal("ChooseShot", method.Name);
        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(AiView), parameter.ParameterType);
        Assert.Equal(typeof(Coordinate), method.ReturnType);
    }
}

public sealed class EasyAiStrategyTests
{
    [Fact]
    public void ChooseShot_NeverRepeatsOver100Shots()
    {
        var view = new AiView();
        var strategy = new EasyAiStrategy(new Random(42));
        var shots = new HashSet<Coordinate>();
        for (var index = 0; index < 100; index++)
        {
            var shot = strategy.ChooseShot(view);
            Assert.True(shots.Add(shot));
            view.RecordShot(shot, ShotOutcome.MISS, null);
        }
    }

    [Fact]
    public void ChooseShot_SameSeed_Deterministic()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.MISS, null);
        Assert.Equal(
            new EasyAiStrategy(new Random(9)).ChooseShot(view),
            new EasyAiStrategy(new Random(9)).ChooseShot(view));
    }

    [Fact]
    public void ChooseShot_DistributionUniformOverUntried()
    {
        var view = new AiView();
        foreach (var cell in Coordinate.AllCells.Skip(4))
        {
            view.RecordShot(cell, ShotOutcome.MISS, null);
        }

        var candidates = Coordinate.AllCells.Take(4).ToArray();
        var counts = candidates.ToDictionary(cell => cell, _ => 0);
        for (var seed = 0; seed < 400; seed++)
        {
            counts[new EasyAiStrategy(new Random(seed)).ChooseShot(view)]++;
        }

        Assert.All(counts.Values, count => Assert.InRange(count, 60, 140));
    }

    [Fact]
    public void ChooseShot_IgnoresHits()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(4, 4), ShotOutcome.HIT, null);
        var picks = new HashSet<Coordinate>();
        for (var seed = 0; seed < 60; seed++)
        {
            picks.Add(new EasyAiStrategy(new Random(seed)).ChooseShot(view));
        }

        Assert.Contains(picks, pick => Math.Abs(pick.Row - 4) + Math.Abs(pick.Col - 4) > 1);
    }
}

public sealed class MediumAiStrategyTests
{
    [Fact]
    public void ChooseShot_NoHits_PicksUntried()
    {
        var view = new AiView();
        var shot = new MediumAiStrategy(new Random(1)).ChooseShot(view);
        Assert.True(view.IsUntried(shot));
    }

    [Fact]
    public void ChooseShot_SingleHit_PicksAdjacent()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(4, 4), ShotOutcome.HIT, null);
        var shot = new MediumAiStrategy(new Random(1)).ChooseShot(view);
        Assert.Equal(1, Math.Abs(shot.Row - 4) + Math.Abs(shot.Col - 4));
    }

    [Fact]
    public void ChooseShot_TwoHitsInRow_ExtendsLine()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(4, 3), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(4, 4), ShotOutcome.HIT, null);
        var shot = new MediumAiStrategy(new Random(1)).ChooseShot(view);
        Assert.Contains(shot, new[] { new Coordinate(4, 2), new Coordinate(4, 5) });
    }

    [Fact]
    public void ChooseShot_TwoHitsInColumn_ExtendsLine()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(3, 4), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(4, 4), ShotOutcome.HIT, null);
        var shot = new MediumAiStrategy(new Random(1)).ChooseShot(view);
        Assert.Contains(shot, new[] { new Coordinate(2, 4), new Coordinate(5, 4) });
    }

    [Fact]
    public void ChooseShot_LineEndsBlocked_FallsBackToAdjacent()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(4, 3), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(4, 4), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(4, 2), ShotOutcome.MISS, null);
        view.RecordShot(new Coordinate(4, 5), ShotOutcome.MISS, null);
        var shot = new MediumAiStrategy(new Random(1)).ChooseShot(view);
        var nearest = view.Hits.Min(hit => Math.Abs(hit.Row - shot.Row) + Math.Abs(hit.Col - shot.Col));
        Assert.Equal(1, nearest);
    }

    [Fact]
    public void ChooseShot_AfterSunkWithNoUnresolvedHits_ReturnsToRandomHunt()
    {
        var view = new AiView();
        foreach (var col in new[] { 1, 2, 3, 4 })
        {
            view.RecordShot(new Coordinate(0, col), ShotOutcome.HIT, null);
        }

        view.RecordShot(new Coordinate(0, 4), ShotOutcome.SUNK, ShipKind.BATTLESHIP);
        Assert.Empty(view.Hits);
        var shot = new MediumAiStrategy(new Random(1)).ChooseShot(view);
        Assert.True(view.IsUntried(shot));
        Assert.DoesNotContain(shot, view.SunkCells);
    }

    [Fact]
    public void ChooseShot_UnresolvedHitsOfOtherShipAfterSunk_KeepsTargeting()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(0, 0), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(0, 1), ShotOutcome.SUNK, ShipKind.PATROL_BOAT);
        view.RecordShot(new Coordinate(4, 4), ShotOutcome.HIT, null);
        var shot = new MediumAiStrategy(new Random(1)).ChooseShot(view);
        Assert.Equal(1, Math.Abs(shot.Row - 4) + Math.Abs(shot.Col - 4));
    }

    [Fact]
    public void ChooseShot_NeverOutOfBoundsOrTried()
    {
        var view = new AiView();
        var strategy = new MediumAiStrategy(new Random(77));
        for (var index = 0; index < 60; index++)
        {
            var shot = strategy.ChooseShot(view);
            Assert.True(shot.IsValid);
            Assert.True(view.IsUntried(shot));
            var outcome = index % 3 == 0 ? ShotOutcome.HIT : ShotOutcome.MISS;
            view.RecordShot(shot, outcome, null);
        }
    }
}

public sealed class HardAiStrategyTests
{
    [Fact]
    public void ChooseShot_NeverRepeats()
    {
        var view = new AiView();
        var strategy = new HardAiStrategy(new Random(12));
        var shots = new HashSet<Coordinate>();
        for (var index = 0; index < 99; index++)
        {
            var shot = strategy.ChooseShot(view);
            Assert.True(shots.Add(shot));
            view.RecordShot(shot, ShotOutcome.MISS, null);
        }
    }

    [Fact]
    public void ChooseShot_PrefersCellsAdjacentToHits()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(4, 4), ShotOutcome.HIT, null);
        var shot = new HardAiStrategy(new Random(3)).ChooseShot(view);
        Assert.Equal(1, Math.Abs(shot.Row - 4) + Math.Abs(shot.Col - 4));
    }

    [Fact]
    public void ChooseShot_PlacementsCoveringHitsOutweighOthers()
    {
        var view = new AiView();
        view.RecordShot(new Coordinate(4, 3), ShotOutcome.HIT, null);
        view.RecordShot(new Coordinate(4, 4), ShotOutcome.HIT, null);
        var shot = new HardAiStrategy(new Random(3)).ChooseShot(view);
        Assert.Equal(1, view.Hits.Min(hit => Math.Abs(hit.Row - shot.Row) + Math.Abs(hit.Col - shot.Col)));
    }

    [Fact]
    public void ChooseShot_EmptyBoard_FavoursHighDensityCells()
    {
        var shot = new HardAiStrategy(new Random(5)).ChooseShot(new AiView());
        Assert.InRange(shot.Row, 1, 8);
        Assert.InRange(shot.Col, 1, 8);
    }

    [Fact]
    public void ChooseShot_ExcludesPlacementsThroughMissesOrSunk()
    {
        var view = new AiView();
        foreach (var cell in Coordinate.AllCells.Where(cell => cell.Row == 0 || cell.Col == 0))
        {
            view.RecordShot(cell, ShotOutcome.MISS, null);
        }

        var shot = new HardAiStrategy(new Random(5)).ChooseShot(view);
        Assert.DoesNotContain(shot, view.Misses);
        Assert.DoesNotContain(shot, view.SunkCells);
        Assert.True(shot.Row > 0 && shot.Col > 0);
    }

    [Fact]
    public void ChooseShot_ConsidersOnlyRemainingShips()
    {
        var view = new AiView();
        foreach (var kind in new[] { ShipKind.CARRIER, ShipKind.BATTLESHIP, ShipKind.CRUISER, ShipKind.SUBMARINE })
        {
            view.RecordShot(new Coordinate(9, 9), ShotOutcome.SUNK, kind);
        }

        Assert.Equal(new[] { ShipKind.PATROL_BOAT }, view.RemainingShips);
        var shot = new HardAiStrategy(new Random(5)).ChooseShot(view);
        Assert.True(view.IsUntried(shot));
    }

    [Fact]
    public void ChooseShot_TiesBrokenRandomly()
    {
        var picks = new HashSet<Coordinate>();
        for (var seed = 0; seed < 30; seed++)
        {
            picks.Add(new HardAiStrategy(new Random(seed)).ChooseShot(new AiView()));
        }

        Assert.True(picks.Count > 1);
    }

    [Fact]
    public void ChooseShot_CompletesWithinDecisionBudget()
    {
        var view = new AiView();
        var watch = Stopwatch.StartNew();
        for (var index = 0; index < 10; index++)
        {
            new HardAiStrategy(new Random(index)).ChooseShot(view);
        }

        watch.Stop();
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(2500));
    }

    [Fact]
    public void AverageShotsToWin_BeatsMediumAndEasy()
    {
        var seeds = Enumerable.Range(0, 6).ToArray();
        var hard = seeds.Average(seed => ShotsToWin(new HardAiStrategy(new Random(seed)), seed));
        var medium = seeds.Average(seed => ShotsToWin(new MediumAiStrategy(new Random(seed)), seed));
        var easy = seeds.Average(seed => ShotsToWin(new EasyAiStrategy(new Random(seed)), seed));
        Assert.True(hard < medium, $"hard={hard:F1} medium={medium:F1}");
        Assert.True(hard < easy, $"hard={hard:F1} easy={easy:F1}");
    }

    private static int ShotsToWin(IAiStrategy strategy, int seed)
    {
        var board = new Board();
        Assert.True(board.TryAutoPlaceAll(new Random(seed + 1000)));
        var resolver = new ShotResolver();
        var view = new AiView();
        var shots = 0;
        while (!board.AllSunk)
        {
            var shot = strategy.ChooseShot(view);
            var result = resolver.Resolve(board, shot);
            view.RecordShot(shot, result.Outcome, result.Ship);
            shots++;
            Assert.True(shots <= 100);
        }

        return shots;
    }
}

public sealed class AiStrategyFactoryTests
{
    [Fact]
    public void Create_Easy_ReturnsEasy() =>
        Assert.IsType<EasyAiStrategy>(AiStrategyFactory.Create("easy", new Random(1)));

    [Fact]
    public void Create_Medium_ReturnsMedium() =>
        Assert.IsType<MediumAiStrategy>(AiStrategyFactory.Create("medium", new Random(1)));

    [Fact]
    public void Create_Hard_ReturnsHard() =>
        Assert.IsType<HardAiStrategy>(AiStrategyFactory.Create("hard", new Random(1)));

    [Fact]
    public void Create_Unknown_ReturnsMedium() =>
        Assert.IsType<MediumAiStrategy>(AiStrategyFactory.Create("impossible", new Random(1)));
}
