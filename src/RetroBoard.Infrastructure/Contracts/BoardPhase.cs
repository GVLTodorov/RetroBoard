namespace RetroBoard.Contracts;

/// <summary>Wire-side mirror of <see cref="Domain.Boards.BoardPhase"/> — kept separate so the
/// Client never needs a reference to the Domain layer.</summary>
public enum BoardPhase
{
    Writing,
    Voting,
    ActionItems,
}
