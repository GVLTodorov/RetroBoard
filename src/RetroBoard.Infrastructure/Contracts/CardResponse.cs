namespace RetroBoard.Contracts;

public sealed record CardResponse(
    Guid CardId,
    string Text,
    Guid AuthorId,
    string AuthorName,
    int? VoteCount,
    int MyVoteCount,
    IReadOnlyList<CardResponse> StackedCards);
