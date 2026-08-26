using Microsoft.AspNetCore.SignalR;
using RetroBoard.Api.Extensions;
using RetroBoard.Api.Services;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Messages;
using RetroBoard.Domain.Boards;

namespace RetroBoard.Api.Hubs;

/// <summary>
/// Realtime surface for a board, one SignalR group per board. Unlike a viewer-agnostic hub, board
/// state genuinely differs per viewer (blur-until-reveal card content, hidden vote counts — see
/// <see cref="Board.GetState"/>), so most mutations broadcast an individualized
/// <see cref="ContractExtensions.ToStateResponse"/> snapshot to every tracked connection rather than
/// one shared group message. <see cref="CastVote"/> is the exception: since nobody else's view
/// changes while votes stay hidden, it replies only to the caller — the cheapest possible broadcast.
/// </summary>
public sealed class BoardHub : Hub
{
    private readonly IBoardRepository _boards;
    private readonly IParticipantTracker _connections;
    private readonly IHubContext<BoardHub> _hubContext;

    public BoardHub(IBoardRepository boards, IParticipantTracker connections, IHubContext<BoardHub> hubContext)
    {
        _boards = boards;
        _connections = connections;
        _hubContext = hubContext;
    }

    public async Task<JoinBoardResponse> JoinBoard(string boardId, string participantName, Guid? existingParticipantId = null)
    {
        var board = GetBoardOrThrow(boardId);

        // Track the connection under the target participant id *before* touching board state, so a
        // concurrent RemoveParticipantIfStillDisconnectedAfterDelayAsync sweep can never observe
        // "reclaimed in Board, but not yet tracked as connected" and remove the participant out from
        // under this very rejoin.
        var participantId = existingParticipantId ?? Guid.NewGuid();
        _connections.Track(Context.ConnectionId, board.Id, participantId);

        var participant = board.AddParticipant(participantName, participantId);

        await Groups.AddToGroupAsync(Context.ConnectionId, board.Id.Value);

        await BroadcastBoardStateAsync(board, excludeConnectionId: Context.ConnectionId);

        return new JoinBoardResponse(participant.Id, board.GetState(participant.Id).ToStateResponse());
    }

    public Task LeaveBoard() => HandleDisconnectAsync();

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await HandleDisconnectAsync();
        await base.OnDisconnectedAsync(exception);
    }

    public async Task AddCard(Guid columnId, string text)
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.AddCard(participantId, columnId, text);
        await BroadcastBoardStateAsync(board);
    }

    public async Task DeleteCard(Guid columnId, Guid cardId)
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.DeleteCard(participantId, columnId, cardId);
        await BroadcastBoardStateAsync(board);
    }

    public async Task MergeCard(Guid columnId, Guid sourceCardId, Guid targetCardId)
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.MergeCard(participantId, columnId, sourceCardId, targetCardId);
        await BroadcastBoardStateAsync(board);
    }

    public async Task RevealColumn(Guid columnId)
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.RevealColumn(participantId, columnId);
        await BroadcastBoardStateAsync(board);
    }

    /// <summary>The hottest path in the app -- every vote cast fans out to just the caller, since
    /// vote counts stay hidden from everyone else until <see cref="EndVoting"/>.</summary>
    public async Task CastVote(Guid cardId, int voteCount)
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.CastVote(participantId, cardId, voteCount);
        await Clients.Caller.SendAsync(Constants.BoardStateChangedEvent, board.GetState(participantId).ToStateResponse());
    }

    public async Task AdvancePhase()
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.AdvancePhase(participantId);
        await BroadcastBoardStateAsync(board);
    }

    public async Task EndVoting()
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.EndVoting(participantId);
        await BroadcastBoardStateAsync(board);
    }

    public async Task ConvertToActionItem(Guid cardId, string? assigneeName, DateOnly? dueDate)
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.ConvertToActionItem(participantId, cardId, assigneeName, dueDate);
        await BroadcastBoardStateAsync(board);
    }

    public async Task StartTimer(int seconds)
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.StartTimer(participantId, seconds);
        await BroadcastBoardStateAsync(board);
    }

    public async Task StopTimer()
    {
        var (board, participantId) = GetTrackedBoardAndParticipant();
        board.StopTimer(participantId);
        await BroadcastBoardStateAsync(board);
    }

    public async Task RemoveParticipant(Guid targetParticipantId)
    {
        var (board, facilitatorId) = GetTrackedBoardAndParticipant();

        // Throws UnauthorizedAccessException (translated to HubException by ExceptionHubFilter)
        // unless the caller is facilitator -- enforced here, not just reflected as a hidden button
        // client-side.
        board.RemoveParticipantAsFacilitator(facilitatorId, targetParticipantId);

        if (_connections.TryGetConnectionId(board.Id, targetParticipantId, out var targetConnectionId))
        {
            _connections.Remove(targetConnectionId);
            await Groups.RemoveFromGroupAsync(targetConnectionId, board.Id.Value);
            await Clients.Client(targetConnectionId).SendAsync(Constants.RemovedFromBoardEvent);
        }

        await BroadcastBoardStateAsync(board);
    }

    private async Task HandleDisconnectAsync()
    {
        if (!_connections.TryGet(Context.ConnectionId, out var info))
        {
            return;
        }

        _connections.Remove(Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, info.BoardId.Value);

        // Don't remove the participant yet -- give a reconnect (JoinBoard with this same participant
        // id) a chance to land first and reclaim their spot, instead of racing a removal against it.
        _ = RemoveParticipantIfStillDisconnectedAfterDelayAsync(info.BoardId, info.ParticipantId);
    }

    // Runs well after the JoinBoard/disconnect invocation that scheduled it has returned, so it must
    // not touch this instance's Clients/Groups/Context -- those are only valid for the lifetime of
    // the triggering hub method call. _hubContext (a real singleton, not tied to any one invocation)
    // is the supported way to broadcast from background work like this.
    private async Task RemoveParticipantIfStillDisconnectedAfterDelayAsync(BoardId boardId, Guid participantId)
    {
        await Task.Delay(ApiConstants.ParticipantReconnectGracePeriod);

        if (_connections.TryGetConnectionId(boardId, participantId, out _))
        {
            // A new connection claimed this participant id in the meantime -- they reconnected.
            return;
        }

        if (!_boards.TryGet(boardId, out var board) || board is null)
        {
            return;
        }

        var isNowEmpty = board.RemoveParticipant(participantId);
        if (isNowEmpty)
        {
            _ = RemoveIfStillEmptyAfterDelayAsync(boardId);
        }
        else
        {
            await BroadcastBoardStateFromContextAsync(board);
        }
    }

    private async Task RemoveIfStillEmptyAfterDelayAsync(BoardId boardId)
    {
        await Task.Delay(ApiConstants.EmptyBoardGracePeriod);

        if (_boards.TryGet(boardId, out var board) && board is not null && board.IsEmpty)
        {
            _boards.Remove(boardId);
        }
    }

    private async Task BroadcastBoardStateAsync(Board board, string? excludeConnectionId = null)
    {
        foreach (var (connectionId, participantId) in _connections.GetConnectionsForBoard(board.Id))
        {
            if (connectionId == excludeConnectionId)
            {
                continue;
            }

            await Clients.Client(connectionId).SendAsync(
                Constants.BoardStateChangedEvent, board.GetState(participantId).ToStateResponse());
        }
    }

    // Same per-viewer broadcast as BroadcastBoardStateAsync, but via _hubContext -- for use from
    // background work outside a hub method invocation (see the comment above
    // RemoveParticipantIfStillDisconnectedAfterDelayAsync).
    private async Task BroadcastBoardStateFromContextAsync(Board board)
    {
        foreach (var (connectionId, participantId) in _connections.GetConnectionsForBoard(board.Id))
        {
            await _hubContext.Clients.Client(connectionId).SendAsync(
                Constants.BoardStateChangedEvent, board.GetState(participantId).ToStateResponse());
        }
    }

    private Board GetBoardOrThrow(string boardId)
    {
        if (!BoardId.TryParse(boardId, out var parsed) || !_boards.TryGet(parsed, out var board) || board is null)
        {
            throw new HubException("Board not found.");
        }

        return board;
    }

    private (Board Board, Guid ParticipantId) GetTrackedBoardAndParticipant()
    {
        if (!_connections.TryGet(Context.ConnectionId, out var info))
        {
            throw new HubException("Not connected to a board.");
        }

        if (!_boards.TryGet(info.BoardId, out var board) || board is null)
        {
            throw new HubException("Board not found.");
        }

        return (board, info.ParticipantId);
    }
}
