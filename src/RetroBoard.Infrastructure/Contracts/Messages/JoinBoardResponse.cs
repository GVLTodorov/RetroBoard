namespace RetroBoard.Contracts.Messages;

public sealed record JoinBoardResponse(Guid ParticipantId, BoardStateResponse State);
