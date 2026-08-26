using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RetroBoard.Client.Services;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Requests;
using RetroBoard.Contracts.Serialization;
using RetroBoard.Tests.Integration.TestSupport;
using Xunit;

namespace RetroBoard.Tests.Integration;

/// <summary>
/// BoardHubClient.cs itself has no internal branches (it's a straight pass-through wrapper), so full
/// line coverage just means exercising every method once through a real connection -- unlike
/// BoardHubTests.cs (which drives the raw HubConnection/hub protocol directly), this drives the
/// wrapper's own public methods and events, using its <c>BoardHubClient(HubConnection)</c>
/// constructor to point it at the same in-memory TestServer.
/// </summary>
public class BoardHubClientTests : IClassFixture<RetroBoardWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = RetroBoardJsonContext.CreateOptions();

    private readonly RetroBoardWebApplicationFactory _factory;

    public BoardHubClientTests(RetroBoardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullLifecycle_ThroughBoardHubClientsOwnMethods_RaisesEveryEvent()
    {
        using var httpClient = _factory.CreateClient();
        var createResponse = await httpClient.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest("Client Lifecycle Board", TemplateType.StartStopContinue, false, 5, 3),
            JsonOptions);
        var board = await createResponse.Content.ReadFromJsonAsync<BoardSummaryResponse>(JsonOptions);
        Assert.NotNull(board);

        await using var alice = NewClient();
        await using var bob = NewClient();

        var aliceStateChanges = new List<BoardStateResponse>();
        alice.BoardStateChanged += s => aliceStateChanges.Add(s);

        var bobWasRemoved = false;
        bob.RemovedFromBoard += () => bobWasRemoved = true;

        Assert.Equal(HubConnectionState.Disconnected, alice.State);
        await alice.StartAsync();
        await bob.StartAsync();
        Assert.Equal(HubConnectionState.Connected, alice.State);

        var aliceJoin = await alice.JoinBoardAsync(board!.BoardId, "Alice");
        var bobJoin = await bob.JoinBoardAsync(board.BoardId, "Bob");
        Assert.Equal(2, bobJoin.State.Participants.Count);

        var columnId = aliceJoin.State.Columns[0].ColumnId;
        await alice.AddCardAsync(columnId, "Alice's card");
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Columns.Single(c => c.ColumnId == columnId).Cards.Count == 1));

        var cardId = aliceStateChanges.Last().Columns.Single(c => c.ColumnId == columnId).Cards[0].CardId;
        await alice.DeleteCardAsync(columnId, cardId);
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Columns.Single(c => c.ColumnId == columnId).Cards.Count == 0));

        await alice.AddCardAsync(columnId, "Card A");
        await alice.AddCardAsync(columnId, "Card B");
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Columns.Single(c => c.ColumnId == columnId).Cards.Count == 2));
        var cards = aliceStateChanges.Last().Columns.Single(c => c.ColumnId == columnId).Cards;

        await alice.MergeCardAsync(columnId, cards[1].CardId, cards[0].CardId);
        await WaitUntilAsync(() => aliceStateChanges.Any(
            s => s.Columns.Single(c => c.ColumnId == columnId).Cards.SingleOrDefault(c => c.CardId == cards[0].CardId)
                is { StackedCards.Count: 1 }));

        await alice.RevealColumnAsync(columnId);
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Columns.Single(c => c.ColumnId == columnId).IsRevealed));

        await alice.AdvancePhaseAsync();
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Phase == BoardPhase.Voting));

        await alice.CastVoteAsync(cards[0].CardId, 2);
        await alice.EndVotingAsync();
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.VotesRevealed));

        await alice.AdvancePhaseAsync();
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Phase == BoardPhase.ActionItems));

        await alice.ConvertToActionItemAsync(cards[0].CardId, "Bob", null);
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.ActionItems.Count == 1));

        await alice.StartTimerAsync(60);
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.TimerEndsAtUtc is not null));

        await alice.StopTimerAsync();
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.TimerEndsAtUtc is null));

        await alice.RemoveParticipantAsync(bobJoin.ParticipantId);
        await WaitUntilAsync(() => bobWasRemoved);

        await alice.LeaveBoardAsync();
    }

    private BoardHubClient NewClient() => new(
        new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/board", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .AddJsonProtocol(options => options.PayloadSerializerOptions = RetroBoardJsonContext.CreateOptions())
            .Build());

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(25);
        }
    }
}
