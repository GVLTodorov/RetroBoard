using BenchmarkDotNet.Attributes;
using RetroBoard.Domain.Boards;
using RetroBoard.Domain.Templates;

namespace RetroBoard.Tests.Benchmarks;

/// <summary>Hot-path domain operations: every card add and vote cast fans out an individualized
/// per-viewer snapshot to every connected participant (see BoardHub.BroadcastBoardStateAsync).</summary>
[MemoryDiagnoser]
public class BoardBenchmarks
{
    private Board _board = null!;
    private Guid _facilitatorId;
    private Guid _columnId;
    private Guid _cardId;

    [GlobalSetup]
    public void Setup()
    {
        BoardId.TryParse("Benchmark Board", out var id);
        _board = new Board(id, "Benchmark Board", TemplateType.WentWellDidntWork, blurUntilReveal: false, voteBudget: 5, maxVotesPerCard: 3);

        var facilitator = _board.AddParticipant("Facilitator");
        _facilitatorId = facilitator.Id;

        for (var i = 1; i < 10; i++)
        {
            _board.AddParticipant($"Participant{i}");
        }

        _columnId = _board.GetState(_facilitatorId).Columns[0].ColumnId;

        for (var i = 0; i < 10; i++)
        {
            var card = _board.AddCard(_facilitatorId, _columnId, $"Card {i}");
            if (i == 0)
            {
                _cardId = card.Id;
            }
        }

        _board.AdvancePhase(_facilitatorId); // Writing -> Voting, needed for CastVote below
    }

    [Benchmark]
    public Card AddCard() => _board.AddCard(_facilitatorId, _columnId, "Benchmark card");

    [Benchmark]
    public void CastVote() => _board.CastVote(_facilitatorId, _cardId, 1);

    [Benchmark]
    public BoardView GetState() => _board.GetState(_facilitatorId);
}
