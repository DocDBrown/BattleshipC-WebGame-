using Battleship.Shared;

namespace Battleship.Web.Game;

public readonly record struct Coordinate(int Row, int Col)
{
    public static readonly IReadOnlyList<Coordinate> AllCells = CreateAllCells();

    public bool IsValid => BoardCoords.InBounds(Row, Col);
    public string Label => BoardCoords.Format(Row, Col);

    public Coordinate Offset(int rowDelta, int colDelta) => new(Row + rowDelta, Col + colDelta);

    public override string ToString() => Label;

    public static bool TryParse(string? text, out Coordinate coordinate)
    {
        if (BoardCoords.TryParse(text, out var row, out var col))
        {
            coordinate = new Coordinate(row, col);
            return true;
        }

        coordinate = default;
        return false;
    }

    private static IReadOnlyList<Coordinate> CreateAllCells()
    {
        var cells = new List<Coordinate>(BoardCoords.Size * BoardCoords.Size);
        for (var row = 0; row < BoardCoords.Size; row++)
        {
            for (var col = 0; col < BoardCoords.Size; col++)
            {
                cells.Add(new Coordinate(row, col));
            }
        }

        return cells;
    }
}
