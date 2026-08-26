namespace RetroBoard.Domain.Boards;

/// <summary>
/// URL-safe board identifier, derived from the board's display name (e.g. "Sprint 12 Retro" ->
/// "sprint-12-retro") so shared board links read as <c>/sprint-12-retro</c> instead of an opaque
/// code. <see cref="TryParse"/> is used both to derive a new board's id from its name at creation
/// time and to normalize an id coming back in from a URL segment — slugifying an already-slugified
/// value is a no-op, so both paths produce the same key.
/// </summary>
public readonly record struct BoardId
{
    private const int MaxLength = 60;

    public string Value { get; }

    private BoardId(string value) => Value = value;

    public static bool TryParse(string? input, out BoardId boardId)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            boardId = default;
            return false;
        }

        Span<char> buffer = stackalloc char[Math.Min(input.Length, MaxLength)];
        var length = 0;
        var lastWasHyphen = false;

        foreach (var ch in input)
        {
            if (length >= buffer.Length)
            {
                break;
            }

            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                buffer[length++] = ch;
                lastWasHyphen = false;
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                buffer[length++] = char.ToLowerInvariant(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && length > 0)
            {
                buffer[length++] = '-';
                lastWasHyphen = true;
            }
        }

        if (lastWasHyphen)
        {
            length--;
        }

        if (length == 0)
        {
            boardId = default;
            return false;
        }

        boardId = new BoardId(new string(buffer[..length]));
        return true;
    }

    public override string ToString() => Value;
}
