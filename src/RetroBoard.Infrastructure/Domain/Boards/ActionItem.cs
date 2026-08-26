namespace RetroBoard.Domain.Boards;

/// <summary>A tracked follow-up produced from a card during the <see cref="BoardPhase.ActionItems"/>
/// phase (see <see cref="Board.ConvertToActionItem"/>). Lives independently of the source column's
/// cards from the moment it's created.</summary>
public sealed record ActionItem(
    Guid Id,
    string Text,
    Guid SourceCardId,
    string? AssigneeName,
    DateOnly? DueDate);
