namespace RetroBoard.Domain.Boards;

/// <summary>Read-only, per-viewer snapshot of an entire <see cref="Board"/> — see
/// <see cref="Board.GetState"/>.</summary>
public sealed record BoardView(
    BoardId BoardId,
    string Name,
    Templates.TemplateType Template,
    BoardPhase Phase,
    bool VotesRevealed,
    bool BlurUntilReveal,
    int VoteBudget,
    int MaxVotesPerCard,
    DateTime? TimerEndsAtUtc,
    IReadOnlyList<ParticipantView> Participants,
    IReadOnlyList<ColumnView> Columns,
    IReadOnlyList<ActionItem> ActionItems);
