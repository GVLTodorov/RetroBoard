namespace RetroBoard.Domain.Boards;

/// <summary>
/// Read-only, per-viewer projection of a <see cref="Card"/>. <see cref="VoteCount"/> is populated
/// only once voting has been revealed for the board (or the board has moved past voting); until
/// then it's <see langword="null"/> for everyone, while <see cref="MyVoteCount"/> — the viewer's own
/// allocation on this card — is always visible to them so they can track their own remaining budget.
/// </summary>
public sealed record CardView(
    Guid CardId,
    string Text,
    Guid AuthorId,
    string AuthorName,
    int? VoteCount,
    int MyVoteCount,
    IReadOnlyList<CardView> StackedCards);
