using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Messages;
using RetroBoard.Contracts.Requests;
using RetroBoard.Contracts.Serialization;
using RetroBoard.Tests.Integration.TestSupport;
using Xunit;

namespace RetroBoard.Tests.Integration;

/// <summary>
/// Drives the whole realtime flow (join -> write -> reveal column -> vote -> end voting -> advance
/// to action items -> convert an action item -> disconnect) end-to-end through the real HTTP +
/// SignalR surface, with no UI involved, plus the facilitator-only rejections for every guarded
/// action.
/// </summary>
public class BoardHubTests : IClassFixture<RetroBoardWebApplicationFactory>
{
    // Mirrors the server's REST JSON options (Program.cs): default System.Net.Http.Json web
    // defaults don't know how to read our string-formatted enums without this.
    private static readonly JsonSerializerOptions JsonOptions = RetroBoardJsonContext.CreateOptions();

    private readonly RetroBoardWebApplicationFactory _factory;

    public BoardHubTests(RetroBoardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullLifecycle_JoinWriteRevealVoteAdvanceConvert_BehavesAsExpected()
    {
        var board = await CreateBoardAsync("Sprint Retro", blurUntilReveal: true, voteBudget: 5, maxVotesPerCard: 3);

        await using var aliceConnection = CreateHubConnection();
        await using var bobConnection = CreateHubConnection();

        var aliceStateChanges = new List<BoardStateResponse>();
        aliceConnection.On<BoardStateResponse>(Constants.BoardStateChangedEvent, s => aliceStateChanges.Add(s));

        await aliceConnection.StartAsync();
        await bobConnection.StartAsync();

        var aliceJoin = await JoinAsync(aliceConnection, board.BoardId, "Alice");
        var bobJoin = await JoinAsync(bobConnection, board.BoardId, "Bob");
        Assert.True(aliceJoin.State.Participants.Single(p => p.ParticipantId == aliceJoin.ParticipantId).IsFacilitator);
        Assert.False(bobJoin.State.Participants.Single(p => p.ParticipantId == bobJoin.ParticipantId).IsFacilitator);

        var columnId = aliceJoin.State.Columns[0].ColumnId;

        await aliceConnection.InvokeAsync("AddCard", columnId, "Alice's card");
        await bobConnection.InvokeAsync("AddCard", columnId, "Bob's card");

        // Under blur-until-reveal, Alice must not see Bob's card text before the column is revealed.
        await WaitUntilAsync(() => aliceStateChanges.Any(s =>
            s.Columns.Single(c => c.ColumnId == columnId).HiddenCardCounts.Any(h => h.AuthorName == "Bob")));
        var beforeReveal = aliceStateChanges.Last(s => s.Columns.Single(c => c.ColumnId == columnId).HiddenCardCounts.Count > 0);
        Assert.Single(beforeReveal.Columns.Single(c => c.ColumnId == columnId).Cards);

        await aliceConnection.InvokeAsync("RevealColumn", columnId);
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Columns.Single(c => c.ColumnId == columnId).Cards.Count == 2));

        await aliceConnection.InvokeAsync("AdvancePhase");
        await bobConnection.InvokeAsync("CastVote",
            aliceStateChanges.Last().Columns.Single(c => c.ColumnId == columnId).Cards.First().CardId, 2);

        await aliceConnection.InvokeAsync("EndVoting");
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.VotesRevealed));

        await aliceConnection.InvokeAsync("AdvancePhase");
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Phase == BoardPhase.ActionItems));

        var cardToConvert = aliceStateChanges.Last().Columns.Single(c => c.ColumnId == columnId).Cards.First();
        await aliceConnection.InvokeAsync("ConvertToActionItem", cardToConvert.CardId, "Bob", (DateOnly?)null);
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.ActionItems.Count == 1));

        await bobConnection.StopAsync();
        await bobConnection.DisposeAsync();

        // ParticipantReconnectGracePeriod (ApiConstants) is 15s; wait comfortably past it.
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Participants.Count == 1), timeoutMs: 20_000);
    }

    [Fact]
    public async Task JoinBoard_WithExistingParticipantId_ReclaimsFacilitatorStatus_AfterReconnect()
    {
        var board = await CreateBoardAsync("Reconnect Board");

        await using var aliceConnection = CreateHubConnection();
        await using var bobConnection = CreateHubConnection();
        await aliceConnection.StartAsync();
        await bobConnection.StartAsync();

        var aliceJoin = await JoinAsync(aliceConnection, board.BoardId, "Alice");
        await JoinAsync(bobConnection, board.BoardId, "Bob");

        // Simulates a page refresh: the old connection drops (no explicit LeaveBoard -- a refresh
        // just tears down the socket) and a brand new connection rejoins with the same participant
        // id while the board still has another participant in it.
        await aliceConnection.StopAsync();

        await using var aliceReconnection = CreateHubConnection();
        await aliceReconnection.StartAsync();
        var rejoin = await JoinAsync(aliceReconnection, board.BoardId, "Alice", aliceJoin.ParticipantId);

        Assert.Equal(aliceJoin.ParticipantId, rejoin.ParticipantId);
        Assert.Equal(2, rejoin.State.Participants.Count);
        Assert.True(rejoin.State.Participants.Single(p => p.ParticipantId == aliceJoin.ParticipantId).IsFacilitator);
    }

    [Fact]
    public async Task RevealColumn_ByNonFacilitator_Throws()
    {
        var board = await CreateBoardAsync("Reveal Rejection Board", blurUntilReveal: true);
        await using var aliceConnection = CreateHubConnection();
        await using var bobConnection = CreateHubConnection();
        await aliceConnection.StartAsync();
        await bobConnection.StartAsync();
        var aliceJoin = await JoinAsync(aliceConnection, board.BoardId, "Alice");
        await JoinAsync(bobConnection, board.BoardId, "Bob");
        var columnId = aliceJoin.State.Columns[0].ColumnId;

        var ex = await Assert.ThrowsAsync<HubException>(
            () => bobConnection.InvokeAsync("RevealColumn", columnId));
        Assert.Contains("facilitator", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdvancePhase_ByNonFacilitator_Throws()
    {
        var board = await CreateBoardAsync("Advance Rejection Board");
        await using var aliceConnection = CreateHubConnection();
        await using var bobConnection = CreateHubConnection();
        await aliceConnection.StartAsync();
        await bobConnection.StartAsync();
        await JoinAsync(aliceConnection, board.BoardId, "Alice");
        await JoinAsync(bobConnection, board.BoardId, "Bob");

        var ex = await Assert.ThrowsAsync<HubException>(() => bobConnection.InvokeAsync("AdvancePhase"));
        Assert.Contains("facilitator", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdvancePhase_ToActionItems_WithoutEndingVoting_Throws()
    {
        var board = await CreateBoardAsync("Skip Voting Board");
        await using var aliceConnection = CreateHubConnection();
        await aliceConnection.StartAsync();
        await JoinAsync(aliceConnection, board.BoardId, "Alice");
        await aliceConnection.InvokeAsync("AdvancePhase");

        var ex = await Assert.ThrowsAsync<HubException>(() => aliceConnection.InvokeAsync("AdvancePhase"));
        Assert.Contains("end voting", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CastVote_AboveBudget_Throws()
    {
        var board = await CreateBoardAsync("Vote Budget Board", voteBudget: 5, maxVotesPerCard: 3);
        await using var aliceConnection = CreateHubConnection();
        var aliceStateChanges = new List<BoardStateResponse>();
        aliceConnection.On<BoardStateResponse>(Constants.BoardStateChangedEvent, s => aliceStateChanges.Add(s));

        await aliceConnection.StartAsync();
        var aliceJoin = await JoinAsync(aliceConnection, board.BoardId, "Alice");
        var columnId = aliceJoin.State.Columns[0].ColumnId;

        await aliceConnection.InvokeAsync("AddCard", columnId, "a");
        await aliceConnection.InvokeAsync("AddCard", columnId, "b");
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Columns.Single(c => c.ColumnId == columnId).Cards.Count == 2));
        var cards = aliceStateChanges.Last().Columns.Single(c => c.ColumnId == columnId).Cards;

        await aliceConnection.InvokeAsync("AdvancePhase");
        await aliceConnection.InvokeAsync("CastVote", cards[0].CardId, 3);

        var ex = await Assert.ThrowsAsync<HubException>(
            () => aliceConnection.InvokeAsync("CastVote", cards[1].CardId, 3));
        Assert.Contains("budget", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveParticipant_ByNonFacilitator_Throws()
    {
        var board = await CreateBoardAsync("Kick Rejection Board");
        await using var aliceConnection = CreateHubConnection();
        await using var bobConnection = CreateHubConnection();
        await aliceConnection.StartAsync();
        await bobConnection.StartAsync();
        var aliceJoin = await JoinAsync(aliceConnection, board.BoardId, "Alice");
        await JoinAsync(bobConnection, board.BoardId, "Bob");

        var ex = await Assert.ThrowsAsync<HubException>(
            () => bobConnection.InvokeAsync("RemoveParticipant", aliceJoin.ParticipantId));
        Assert.Contains("facilitator", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemoveParticipant_ByFacilitator_DisconnectsTargetAndUpdatesBoardState()
    {
        var board = await CreateBoardAsync("Kick Board");
        await using var aliceConnection = CreateHubConnection();
        await using var bobConnection = CreateHubConnection();

        var bobWasRemoved = false;
        bobConnection.On(Constants.RemovedFromBoardEvent, () => bobWasRemoved = true);

        var aliceStateChanges = new List<BoardStateResponse>();
        aliceConnection.On<BoardStateResponse>(Constants.BoardStateChangedEvent, s => aliceStateChanges.Add(s));

        await aliceConnection.StartAsync();
        await bobConnection.StartAsync();

        var aliceJoin = await JoinAsync(aliceConnection, board.BoardId, "Alice");
        var bobJoin = await JoinAsync(bobConnection, board.BoardId, "Bob");

        await aliceConnection.InvokeAsync("RemoveParticipant", bobJoin.ParticipantId);

        await WaitUntilAsync(() => bobWasRemoved);
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Participants.Count == 1));
        Assert.Equal(aliceJoin.ParticipantId, aliceStateChanges.Last().Participants.Single().ParticipantId);
    }

    [Fact]
    public async Task JoinBoard_Throws_WhenBoardIdIsBogus()
    {
        await using var connection = CreateHubConnection();
        await connection.StartAsync();

        var ex = await Assert.ThrowsAsync<HubException>(() => JoinAsync(connection, "no-such-board", "Alice"));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnyHubMethod_Throws_WhenCalledBeforeJoiningABoard()
    {
        await using var connection = CreateHubConnection();
        await connection.StartAsync();

        var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("AdvancePhase"));
        Assert.Contains("not connected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LeaveBoard_UntracksTheParticipant_SoFurtherCallsOnThatConnectionAreRejected()
    {
        var board = await CreateBoardAsync("Leave Board Test");
        await using var connection = CreateHubConnection();
        await connection.StartAsync();
        await JoinAsync(connection, board.BoardId, "Alice");

        await connection.InvokeAsync("LeaveBoard");

        var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("AdvancePhase"));
        Assert.Contains("not connected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<BoardSummaryResponse> CreateBoardAsync(
        string name, bool blurUntilReveal = false, int? voteBudget = null, int? maxVotesPerCard = null)
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest(name, TemplateType.StartStopContinue, blurUntilReveal, voteBudget, maxVotesPerCard),
            JsonOptions);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BoardSummaryResponse>(JsonOptions))!;
    }

    private static Task<JoinBoardResponse> JoinAsync(
        HubConnection connection, string boardId, string participantName, Guid? existingParticipantId = null) =>
        connection.InvokeAsync<JoinBoardResponse>("JoinBoard", boardId, participantName, existingParticipantId);

    private HubConnection CreateHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/board", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                // WebSocket transport uses ClientWebSocket directly, bypassing the injected
                // handler, so it can't reach TestServer's in-memory endpoint -- long polling can.
                options.Transports = HttpTransportType.LongPolling;
            })
            // Must mirror the server's hub JSON options (Program.cs) so string-formatted enums
            // round-trip correctly and plain (non-model) argument types still resolve instead of
            // throwing NotSupportedException.
            .AddJsonProtocol(options => options.PayloadSerializerOptions = RetroBoardJsonContext.CreateOptions())
            .Build();

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
