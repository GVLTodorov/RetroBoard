namespace RetroBoard.Contracts;

public sealed record BoardStateResponse(
    string BoardId,
    string Name,
    TemplateType Template,
    BoardPhase Phase,
    bool VotesRevealed,
    bool BlurUntilReveal,
    int VoteBudget,
    int MaxVotesPerCard,
    DateTime? TimerEndsAtUtc,
    IReadOnlyList<ParticipantResponse> Participants,
    IReadOnlyList<ColumnResponse> Columns,
    IReadOnlyList<ActionItemResponse> ActionItems);
