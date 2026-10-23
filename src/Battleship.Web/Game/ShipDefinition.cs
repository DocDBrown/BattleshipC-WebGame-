using Battleship.Shared;

namespace Battleship.Web.Game;

public sealed record ShipDefinition(ShipKind Kind, string Name, int Size);

public static class Fleet
{
    public static readonly IReadOnlyList<ShipDefinition> Ships = new[]
    {
        new ShipDefinition(ShipKind.CARRIER, "Carrier", 5),
        new ShipDefinition(ShipKind.BATTLESHIP, "Battleship", 4),
        new ShipDefinition(ShipKind.CRUISER, "Cruiser", 3),
        new ShipDefinition(ShipKind.SUBMARINE, "Submarine", 3),
        new ShipDefinition(ShipKind.PATROL_BOAT, "Patrol Boat", 2)
    };

    public static ShipDefinition Get(ShipKind kind) =>
        Ships.First(s => s.Kind == kind);

    public static int SizeOf(ShipKind kind) => Get(kind).Size;

    public static string NameOf(ShipKind kind) => Get(kind).Name;
}
