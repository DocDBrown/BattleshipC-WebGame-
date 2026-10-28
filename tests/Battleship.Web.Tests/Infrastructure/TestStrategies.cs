using Battleship.Web.Ai;
using Battleship.Web.Game;

namespace Battleship.Web.Tests.Infrastructure;

internal sealed class RecordingStrategy : IAiStrategy
{
    public List<AiView> Views { get; } = new();

    public Action<AiView>? OnChoose { get; set; }

    public Func<AiView, Coordinate>? Picker { get; set; }

    public Coordinate ChooseShot(AiView view)
    {
        Views.Add(view);
        OnChoose?.Invoke(view);
        return Picker?.Invoke(view) ?? view.Untried()[0];
    }
}

internal sealed class FixedStrategy : IAiStrategy
{
    private readonly Coordinate _coordinate;

    public FixedStrategy(Coordinate coordinate) => _coordinate = coordinate;

    public Coordinate ChooseShot(AiView view) =>
        view.IsUntried(_coordinate) ? _coordinate : view.Untried()[0];
}

internal sealed class SlowStrategy : IAiStrategy
{
    private readonly TimeSpan _delay;

    public SlowStrategy(TimeSpan delay) => _delay = delay;

    public Coordinate ChooseShot(AiView view)
    {
        Thread.Sleep(_delay);
        return view.Untried()[0];
    }
}

internal sealed class EmptyRowStrategy : IAiStrategy
{
    public Coordinate ChooseShot(AiView view) =>
        view.Untried().First(cell => cell.Row >= 5);
}

internal sealed class ZeroRandom : Random
{
    public override int Next(int maxValue) => 0;

    public override int Next(int minValue, int maxValue) => minValue;
}
