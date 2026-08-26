namespace RetroBoard.Contracts;

public sealed record ActionItemResponse(
    Guid ActionItemId, string Text, Guid SourceCardId, string? AssigneeName, DateOnly? DueDate);
