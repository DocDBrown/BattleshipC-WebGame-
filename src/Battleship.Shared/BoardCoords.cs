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

        var numberText = trimmed.AsSpan(1);
        int number;
        if (numberText.Length == 1)
        {
            if (numberText[0] is < '1' or > '9')
            {
                return false;
            }

            number = numberText[0] - '0';
        }
        else if (numberText.Length == 2 && numberText[0] == '1' && numberText[1] == '0')
        {
            number = 10;
        }
        else
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
