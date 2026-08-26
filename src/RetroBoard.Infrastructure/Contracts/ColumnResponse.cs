namespace RetroBoard.Contracts;

public sealed record ColumnResponse(
    Guid ColumnId,
    string Title,
    bool IsRevealed,
    IReadOnlyList<CardResponse> Cards,
    IReadOnlyList<AuthorCardCountResponse> HiddenCardCounts);
