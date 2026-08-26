using System.Text.Json;
using Microsoft.JSInterop;

namespace RetroBoard.Client.Services;

/// <summary>
/// Holds the identity the participant picked on the create/join screen so Board.razor doesn't need
/// to re-prompt for it. Registered scoped; in a WASM app that's effectively "this browser tab" --
/// except a full page refresh also tears down and re-creates that scope, which is why the identity
/// is additionally mirrored into sessionStorage and restored via <see cref="RestoreAsync"/>, so
/// refreshing mid-board reconnects instead of bouncing back to the join screen (§5.6).
/// </summary>
public sealed class ParticipantSessionState
{
    private const string StorageKey = "retroboard.session";

    private readonly IJSRuntime _jsRuntime;
    private IJSObjectReference? _jsModule;

    public ParticipantSessionState(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public string ParticipantName { get; set; } = string.Empty;

    /// <summary>Board the stored <see cref="ParticipantId"/> belongs to, so it's only reused for a
    /// refresh of that same board -- not carried over if the tab later visits a different one.</summary>
    public string? BoardId { get; set; }

    /// <summary>Set once the board page successfully joins a board, so a page refresh can rejoin as
    /// this same participant (see <c>BoardHub.JoinBoard</c>'s <c>existingParticipantId</c>) instead
    /// of appearing as a brand new one -- which would silently cost a refreshing facilitator their
    /// facilitator status.</summary>
    public Guid? ParticipantId { get; set; }

    public async Task RestoreAsync()
    {
        if (!string.IsNullOrWhiteSpace(ParticipantName))
        {
            return;
        }

        var module = await GetModuleAsync();
        var json = await module.InvokeAsync<string?>("loadSessionItem", StorageKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var stored = JsonSerializer.Deserialize<StoredSession>(json);
        if (stored is null)
        {
            return;
        }

        ParticipantName = stored.ParticipantName;
        BoardId = stored.BoardId;
        ParticipantId = stored.ParticipantId;
    }

    public async Task SaveAsync()
    {
        var module = await GetModuleAsync();
        var json = JsonSerializer.Serialize(new StoredSession(ParticipantName, BoardId, ParticipantId));
        await module.InvokeVoidAsync("saveSessionItem", StorageKey, json);
    }

    private async Task<IJSObjectReference> GetModuleAsync() =>
        _jsModule ??= await _jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/interop.js");

    private sealed record StoredSession(string ParticipantName, string? BoardId, Guid? ParticipantId);
}
