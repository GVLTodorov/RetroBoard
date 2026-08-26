using Microsoft.AspNetCore.SignalR.Client;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Messages;

namespace RetroBoard.Client.Services;

/// <summary>Abstraction over <see cref="BoardHubClient"/> so consumers (e.g. <c>Board.razor</c>) can
/// be driven by a test double instead of a real SignalR connection.</summary>
public interface IBoardHubClient : IAsyncDisposable
{
    event Action<BoardStateResponse>? BoardStateChanged;
    event Action? RemovedFromBoard;
    event Action? Reconnected;

    HubConnectionState State { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task<JoinBoardResponse> JoinBoardAsync(string boardId, string participantName, Guid? existingParticipantId = null);

    Task LeaveBoardAsync();

    Task AddCardAsync(Guid columnId, string text);

    Task DeleteCardAsync(Guid columnId, Guid cardId);

    Task MergeCardAsync(Guid columnId, Guid sourceCardId, Guid targetCardId);

    Task RevealColumnAsync(Guid columnId);

    Task CastVoteAsync(Guid cardId, int voteCount);

    Task AdvancePhaseAsync();

    Task EndVotingAsync();

    Task ConvertToActionItemAsync(Guid cardId, string? assigneeName, DateOnly? dueDate);

    Task StartTimerAsync(int seconds);

    Task StopTimerAsync();

    Task RemoveParticipantAsync(Guid targetParticipantId);
}
