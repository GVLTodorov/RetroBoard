using System.Text.Json;
using BenchmarkDotNet.Attributes;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Serialization;

namespace RetroBoard.Tests.Benchmarks;

/// <summary>
/// Justifies the source-gen JSON context (REQUIREMENTS.MD Section 9): the per-viewer board-state
/// snapshot is the hottest path in the app (every card add/vote fans one out to every connected
/// participant), so this measures the win over plain reflection-based serialization for a
/// representative <see cref="BoardStateResponse"/> payload.
/// </summary>
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private static readonly JsonSerializerOptions ReflectionOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions SourceGenOptions = RetroBoardJsonContext.CreateOptions();

    private BoardStateResponse _state = null!;

    [GlobalSetup]
    public void Setup()
    {
        var participants = Enumerable.Range(0, 10)
            .Select(i => new ParticipantResponse(Guid.NewGuid(), $"Participant{i}", IsFacilitator: i == 0))
            .ToList();

        var columns = new[] { "Went well", "Didn't go well", "Action items" }
            .Select(title => new ColumnResponse(
                Guid.NewGuid(),
                title,
                IsRevealed: true,
                Cards: Enumerable.Range(0, 10)
                    .Select(i => new CardResponse(
                        Guid.NewGuid(), $"Card text {i}", Guid.NewGuid(), $"Participant{i}", VoteCount: i, MyVoteCount: 1, StackedCards: []))
                    .ToList(),
                HiddenCardCounts: []))
            .ToList();

        _state = new BoardStateResponse(
            "benchmark-board", "Benchmark Board", TemplateType.WentWellDidntWork, BoardPhase.Voting,
            VotesRevealed: true, BlurUntilReveal: false, VoteBudget: 5, MaxVotesPerCard: 3, TimerEndsAtUtc: null,
            participants, columns, ActionItems: []);
    }

    [Benchmark(Baseline = true)]
    public string Reflection() => JsonSerializer.Serialize(_state, ReflectionOptions);

    [Benchmark]
    public string SourceGenerated() => JsonSerializer.Serialize(_state, SourceGenOptions);
}
