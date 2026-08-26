namespace RetroBoard.Domain.Boards;

/// <summary>Read-only projection of a <see cref="Participant"/> for broadcast to clients.</summary>
public sealed record ParticipantView(Guid ParticipantId, string Name, bool IsFacilitator);
