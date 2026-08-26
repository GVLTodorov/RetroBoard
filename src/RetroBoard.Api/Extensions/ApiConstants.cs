namespace RetroBoard.Api.Extensions;

/// <summary>
/// The grace windows <see cref="Hubs.BoardHub"/> waits before treating a disconnect as final -- long
/// enough to cover a page refresh's brief drop-then-reconnect.
/// </summary>
public static class ApiConstants
{
    /// <summary>How long an empty board is kept around before deletion, in case its last participant
    /// is mid-refresh rather than gone for good.</summary>
    public static readonly TimeSpan EmptyBoardGracePeriod = TimeSpan.FromSeconds(15);

    /// <summary>How long a disconnected participant is kept on the board before being removed, in
    /// case they reconnect (same board, same participant id) and reclaim their identity instead.</summary>
    public static readonly TimeSpan ParticipantReconnectGracePeriod = TimeSpan.FromSeconds(15);
}
