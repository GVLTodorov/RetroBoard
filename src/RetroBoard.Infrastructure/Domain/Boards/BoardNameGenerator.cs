namespace RetroBoard.Domain.Boards;

/// <summary>
/// Generates a friendly "&lt;word&gt;-&lt;shortid&gt;" display-name suggestion (e.g. "calm-a3f9k2")
/// for the create-board screen. This is a display label only — unrelated to <see cref="BoardId"/>,
/// which is the actual identifier used in board URLs.
/// </summary>
public static class BoardNameGenerator
{
    private static readonly string[] Words =
    [
        "calm", "clever", "brave", "swift", "eager", "gentle", "bold", "bright",
        "cosmic", "quiet", "vivid", "witty", "sunny", "lively", "nimble", "keen",
        "jolly", "mellow", "plucky", "sturdy", "breezy", "dapper", "earnest", "fuzzy",
        "golden", "honest", "iconic", "jovial", "kindly", "lucid", "merry", "noble",
        "peppy", "quick", "royal", "shiny", "tidy", "upbeat", "wild", "zesty",
    ];

    private const string IdAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
    private const int IdLength = 6;

    public static string Generate()
    {
        var word = Words[Random.Shared.Next(Words.Length)];

        Span<char> idBuffer = stackalloc char[IdLength];
        for (var i = 0; i < IdLength; i++)
        {
            idBuffer[i] = IdAlphabet[Random.Shared.Next(IdAlphabet.Length)];
        }

        return $"{word}-{new string(idBuffer)}";
    }
}
