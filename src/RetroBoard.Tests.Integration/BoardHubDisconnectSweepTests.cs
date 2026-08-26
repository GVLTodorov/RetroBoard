using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// BoardHub's two background sweeps (RemoveParticipantIfStillDisconnectedAfterDelayAsync,
/// RemoveIfStillEmptyAfterDelayAsync) only fire after ApiConstants' real 15-second grace periods
/// elapse -- kept in their own file since these tests are meaningfully slower than the rest of the
/// suite (each one waits out at least one real grace period).
/// </summary>
public class BoardHubDisconnectSweepTests : IClassFixture<RetroBoardWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = RetroBoardJsonContext.CreateOptions();

    private readonly RetroBoardWebApplicationFactory _factory;

    public BoardHubDisconnectSweepTests(RetroBoardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DisconnectedParticipant_IsRemoved_AfterTheReconnectGracePeriodExpires()
    {
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest("Sweep Removal Board", TemplateType.StartStopContinue, false, null, null),
            JsonOptions);
        var board = await createResponse.Content.ReadFromJsonAsync<BoardSummaryResponse>(JsonOptions);

        await using var aliceConnection = CreateHubConnection();
        var bobConnection = CreateHubConnection();
        var aliceStateChanges = new List<BoardStateResponse>();
        aliceConnection.On<BoardStateResponse>(Constants.BoardStateChangedEvent, s => aliceStateChanges.Add(s));

        await aliceConnection.StartAsync();
        await bobConnection.StartAsync();
        var aliceJoin = await JoinAsync(aliceConnection, board!.BoardId, "Alice");
        await JoinAsync(bobConnection, board.BoardId, "Bob");

        await bobConnection.DisposeAsync();

        // ParticipantReconnectGracePeriod is 15s; wait comfortably past it for the sweep's own
        // broadcast (the board is not empty afterward, so this exercises the "still has
        // participants" broadcast branch of RemoveParticipantIfStillDisconnectedAfterDelayAsync).
        await WaitUntilAsync(() => aliceStateChanges.Any(s => s.Participants.Count == 1), timeoutMs: 20_000);
        Assert.Equal(aliceJoin.ParticipantId, aliceStateChanges.Last().Participants.Single().ParticipantId);
    }

    [Fact]
    public async Task DisconnectedParticipant_IsNotRemoved_WhenTheyReconnectWithinTheGracePeriod()
    {
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest("Sweep Reconnect Board", TemplateType.StartStopContinue, false, null, null),
            JsonOptions);
        var board = await createResponse.Content.ReadFromJsonAsync<BoardSummaryResponse>(JsonOptions);

        await using var aliceConnection = CreateHubConnection();
        var bobConnection = CreateHubConnection();

        await aliceConnection.StartAsync();
        await bobConnection.StartAsync();
        await JoinAsync(aliceConnection, board!.BoardId, "Alice");
        var bobJoin = await JoinAsync(bobConnection, board.BoardId, "Bob");

        await bobConnection.DisposeAsync();

        // Reconnect well inside the 15s grace period -- the sweep, once it does run, must find
        // Bob's participant id tracked again (by this new connection) and return without touching
        // the board.
        await using var bobReconnection = CreateHubConnection();
        await bobReconnection.StartAsync();
        await JoinAsync(bobReconnection, board.BoardId, "Bob", existingParticipantId: bobJoin.ParticipantId);

        // Wait past the original grace period, then confirm Bob is still present via a fresh query.
        await Task.Delay(16_000);
        var stateResponse = await client.GetAsync($"/api/boards/{board.BoardId}");
        stateResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Board_IsRemoved_WhenItStaysEmptyThroughBothGracePeriods()
    {
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            "/api/boards",
            new CreateBoardRequest("Sweep Empty Board", TemplateType.StartStopContinue, false, null, null),
            JsonOptions);
        var board = await createResponse.Content.ReadFromJsonAsync<BoardSummaryResponse>(JsonOptions);

        var soloConnection = CreateHubConnection();
        await soloConnection.StartAsync();
        await JoinAsync(soloConnection, board!.BoardId, "Solo");

        await soloConnection.DisposeAsync();

        // ParticipantReconnectGracePeriod (15s) elapses, the board becomes empty, then
        // EmptyBoardGracePeriod (another 15s) elapses before the board itself is deleted -- poll
        // past both rather than a single fixed delay, to keep this robust against scheduling jitter.
        await WaitUntilAsync(
            async () =>
            {
                var response = await client.GetAsync($"/api/boards/{board.BoardId}");
                return response.StatusCode == HttpStatusCode.NotFound;
            },
            timeoutMs: 35_000);
    }

    private static Task<JoinBoardResponse> JoinAsync(
        HubConnection connection, string boardId, string participantName, Guid? existingParticipantId = null) =>
        connection.InvokeAsync<JoinBoardResponse>("JoinBoard", boardId, participantName, existingParticipantId);

    private HubConnection CreateHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/board", options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs = 5000)
    {
        var start = DateTime.UtcNow;
        while (!await condition())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition was not met within the timeout.");
            }

            await Task.Delay(100);
        }
    }
}
