namespace RetroBoard.Domain.Boards;

/// <summary>The "N cards from Author" placeholder shown for another participant's cards in an
/// unrevealed, blur-until-reveal column (see <see cref="Board.BlurUntilReveal"/>).</summary>
public sealed record AuthorCardCount(string AuthorName, int Count);
