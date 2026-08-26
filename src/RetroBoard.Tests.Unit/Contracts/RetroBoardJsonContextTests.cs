using System.Text.Json;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Messages;
using RetroBoard.Contracts.Requests;
using RetroBoard.Contracts.Serialization;

namespace RetroBoard.Tests.Unit.Contracts;

public class RetroBoardJsonContextTests
{
    private static readonly JsonSerializerOptions Options = RetroBoardJsonContext.CreateOptions();

    [Fact]
    public void BoardStateResponse_RoundTrips()
    {
        var card = new CardResponse(Guid.NewGuid(), "Ship it", Guid.NewGuid(), "Alice", 3, 1, []);
        var column = new ColumnResponse(
            Guid.NewGuid(), "Went well", true, [card], [new AuthorCardCountResponse("Bob", 2)]);
        var actionItem = new ActionItemResponse(
            Guid.NewGuid(), "Automate deploys", card.CardId, "Bob", new DateOnly(2026, 9, 1));
        var state = new BoardStateResponse(
            "sprint-retro", "Sprint Retro", TemplateType.WentWellDidntWork, BoardPhase.Voting, false, true, 5, 3,
            DateTime.UtcNow, [new ParticipantResponse(Guid.NewGuid(), "Alice", true)], [column], [actionItem]);

        var json = JsonSerializer.Serialize(state, Options);
        var roundTripped = JsonSerializer.Deserialize<BoardStateResponse>(json, Options);

        // Assert.Equivalent, not Assert.Equal: the record's IReadOnlyList<T> properties deserialize
        // as a different concrete List<T> instance than the collection-expression literals used to
        // build `state`, and List<T> doesn't override Equals -- record-generated equality would
        // compare those by reference and always report unequal even when structurally identical.
        Assert.Equivalent(state, roundTripped);
    }

    [Fact]
    public void JoinBoardResponse_RoundTrips()
    {
        var state = new BoardStateResponse(
            "sprint-retro", "Sprint Retro", TemplateType.MadSadGlad, BoardPhase.Writing, false, false, 5, 3,
            null, [], [], []);
        var response = new JoinBoardResponse(Guid.NewGuid(), state);

        var json = JsonSerializer.Serialize(response, Options);
        var roundTripped = JsonSerializer.Deserialize<JoinBoardResponse>(json, Options);

        Assert.Equivalent(response, roundTripped);
    }

    [Fact]
    public void CreateBoardRequest_RoundTrips()
    {
        var request = new CreateBoardRequest("Sprint Retro", TemplateType.FourLs, true, 8, 4);

        var json = JsonSerializer.Serialize(request, Options);
        var roundTripped = JsonSerializer.Deserialize<CreateBoardRequest>(json, Options);

        Assert.Equal(request, roundTripped);
    }

    [Fact]
    public void TemplateType_SerializesAsAString_NotANumber()
    {
        var request = new CreateBoardRequest("Sprint Retro", TemplateType.StartStopContinue, false, null, null);

        var json = JsonSerializer.Serialize(request, Options);

        Assert.Contains("\"template\":\"StartStopContinue\"", json);
    }
}
