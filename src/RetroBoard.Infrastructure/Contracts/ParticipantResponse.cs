namespace RetroBoard.Contracts;

public sealed record ParticipantResponse(Guid ParticipantId, string Name, bool IsFacilitator);
