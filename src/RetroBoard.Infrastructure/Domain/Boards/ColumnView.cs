namespace RetroBoard.Domain.Boards;

/// <summary>
/// Read-only, per-viewer projection of a <see cref="Column"/>. While the column is unrevealed under
/// blur-until-reveal, <see cref="VisibleCards"/> holds only the viewer's own cards and
/// <see cref="HiddenCardCounts"/> summarizes everyone else's as an author/count placeholder; once
/// revealed (or blur-until-reveal is off), every card is in <see cref="VisibleCards"/> and
/// <see cref="HiddenCardCounts"/> is empty.
/// </summary>
public sealed record ColumnView(
    Guid ColumnId,
    string Title,
    bool IsRevealed,
    IReadOnlyList<CardView> VisibleCards,
    IReadOnlyList<AuthorCardCount> HiddenCardCounts);
