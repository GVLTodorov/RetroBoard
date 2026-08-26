namespace RetroBoard.Domain.Boards;

/// <summary>A board's lifecycle stage — advanced strictly forward by the facilitator
/// (see <see cref="Board.AdvancePhase"/>), never backward.</summary>
public enum BoardPhase
{
    Writing,
    Voting,
    ActionItems,
}
