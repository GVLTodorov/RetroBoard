using System.Collections.Concurrent;
using RetroBoard.Domain.Boards;

namespace RetroBoard.Api.Services;

public sealed class ParticipantTracker : IParticipantTracker
{
    private readonly ConcurrentDictionary<string, (BoardId BoardId, Guid ParticipantId)> _connections = new();

    public void Track(string connectionId, BoardId boardId, Guid participantId) =>
        _connections[connectionId] = (boardId, participantId);

    public bool TryGet(string connectionId, out (BoardId BoardId, Guid ParticipantId) info) =>
        _connections.TryGetValue(connectionId, out info);

    public bool TryGetConnectionId(BoardId boardId, Guid participantId, out string connectionId)
    {
        foreach (var (candidateConnectionId, info) in _connections)
        {
            if (info.BoardId.Equals(boardId) && info.ParticipantId == participantId)
            {
                connectionId = candidateConnectionId;
                return true;
            }
        }

        connectionId = string.Empty;
        return false;
    }

    public IReadOnlyList<(string ConnectionId, Guid ParticipantId)> GetConnectionsForBoard(BoardId boardId) =>
        _connections
            .Where(kv => kv.Value.BoardId.Equals(boardId))
            .Select(kv => (kv.Key, kv.Value.ParticipantId))
            .ToList();

    public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);
}

/// <summary>
/// Maps a SignalR connection to the board/participant it joined, so hub methods after JoinBoard
/// don't need the caller to keep re-supplying the board id. No reconnect grace period of its own:
/// a dropped connection is simply forgotten here; <see cref="Hubs.BoardHub"/> owns the actual
/// participant-removal grace period.
/// </summary>
public interface IParticipantTracker
{
    void Track(string connectionId, BoardId boardId, Guid participantId);

    bool TryGet(string connectionId, out (BoardId BoardId, Guid ParticipantId) info);

    bool TryGetConnectionId(BoardId boardId, Guid participantId, out string connectionId);

    /// <summary>Every connection currently tracked as being on <paramref name="boardId"/> — used to
    /// broadcast a per-viewer <see cref="Domain.Boards.BoardView"/> to each connected participant
    /// individually, since board state isn't the same for every viewer (see §5.4.1/§5.4.3).</summary>
    IReadOnlyList<(string ConnectionId, Guid ParticipantId)> GetConnectionsForBoard(BoardId boardId);

    void Remove(string connectionId);
}
