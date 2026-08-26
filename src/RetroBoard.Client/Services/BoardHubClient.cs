using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Messages;
using RetroBoard.Contracts.Serialization;

namespace RetroBoard.Client.Services;

/// <summary>Thin wrapper over a <see cref="HubConnection"/> to <c>Api/Hubs/BoardHub.cs</c>.</summary>
public sealed class BoardHubClient : IBoardHubClient
{
    private readonly HubConnection _connection;

    public event Action<BoardStateResponse>? BoardStateChanged;
    public event Action? RemovedFromBoard;

    public BoardHubClient(NavigationManager navigation)
        : this(new HubConnectionBuilder()
            .WithUrl(navigation.ToAbsoluteUri("/hubs/board"))
            .WithAutomaticReconnect()
            .AddJsonProtocol(options => options.PayloadSerializerOptions = RetroBoardJsonContext.CreateOptions())
            .Build())
    {
    }

    /// <summary>Accepts an already-built <see cref="HubConnection"/> directly. Used by
    /// RetroBoard.Tests.Integration to point this wrapper's real event plumbing at an in-memory
    /// TestServer instead of a live browser-hosted URL -- HttpConnectionOptions itself isn't
    /// referenceable from this Blazor WebAssembly project's compile target, so callers configure the
    /// connection themselves and hand it in already-built.</summary>
    public BoardHubClient(HubConnection connection)
    {
        _connection = connection;

        _connection.On<BoardStateResponse>(Constants.BoardStateChangedEvent, state => BoardStateChanged?.Invoke(state));
        _connection.On(Constants.RemovedFromBoardEvent, () => RemovedFromBoard?.Invoke());
    }

    public HubConnectionState State => _connection.State;

    public Task StartAsync(CancellationToken cancellationToken = default) => _connection.StartAsync(cancellationToken);

    public Task<JoinBoardResponse> JoinBoardAsync(string boardId, string participantName, Guid? existingParticipantId = null) =>
        _connection.InvokeAsync<JoinBoardResponse>("JoinBoard", boardId, participantName, existingParticipantId);

    public Task LeaveBoardAsync() => _connection.InvokeAsync("LeaveBoard");

    public Task AddCardAsync(Guid columnId, string text) => _connection.InvokeAsync("AddCard", columnId, text);

    public Task DeleteCardAsync(Guid columnId, Guid cardId) => _connection.InvokeAsync("DeleteCard", columnId, cardId);

    public Task MergeCardAsync(Guid columnId, Guid sourceCardId, Guid targetCardId) =>
        _connection.InvokeAsync("MergeCard", columnId, sourceCardId, targetCardId);

    public Task RevealColumnAsync(Guid columnId) => _connection.InvokeAsync("RevealColumn", columnId);

    public Task CastVoteAsync(Guid cardId, int voteCount) => _connection.InvokeAsync("CastVote", cardId, voteCount);

    public Task AdvancePhaseAsync() => _connection.InvokeAsync("AdvancePhase");

    public Task EndVotingAsync() => _connection.InvokeAsync("EndVoting");

    public Task ConvertToActionItemAsync(Guid cardId, string? assigneeName, DateOnly? dueDate) =>
        _connection.InvokeAsync("ConvertToActionItem", cardId, assigneeName, dueDate);

    public Task StartTimerAsync(int seconds) => _connection.InvokeAsync("StartTimer", seconds);

    public Task StopTimerAsync() => _connection.InvokeAsync("StopTimer");

    public Task RemoveParticipantAsync(Guid targetParticipantId) =>
        _connection.InvokeAsync("RemoveParticipant", targetParticipantId);

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
