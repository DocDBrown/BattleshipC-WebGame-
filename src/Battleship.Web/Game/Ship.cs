using Battleship.Shared;

namespace Battleship.Web.Game;

public sealed class Ship
{
    public Ship(ShipDefinition definition, IReadOnlyList<Coordinate> cells, bool horizontal)
    {
        Definition = definition;
        Cells = cells;
        Horizontal = horizontal;
    }

    public ShipDefinition Definition { get; }
    public IReadOnlyList<Coordinate> Cells { get; }
    public bool Horizontal { get; }
    public HashSet<Coordinate> Hits { get; } = new();

    public ShipKind Kind => Definition.Kind;
    public string Name => Definition.Name;
    public int Size => Definition.Size;
    public bool IsSunk => Hits.Count >= Cells.Count;
}
