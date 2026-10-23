namespace Battleship.Shared;

public static class BoardCoords
{
    public const int Size = 10;

    public static bool TryParse(string? text, out int row, out int col)
    {
        row = -1;
        col = -1;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim().ToUpperInvariant();
        if (trimmed.Length is < 2 or > 3)
        {
            return false;
        }

        var letter = trimmed[0];
        if (letter is < 'A' or > 'J')
        {
            return false;
        }

        if (!int.TryParse(trimmed.AsSpan(1), out var number) || number is < 1 or > 10)
        {
            return false;
        }

        row = letter - 'A';
        col = number - 1;
        return true;
    }

    public static bool InBounds(int row, int col) =>
        row is >= 0 and < Size && col is >= 0 and < Size;

    public static string Format(int row, int col) => ProtocolInfo.FormatCoordinate(row, col);
}
