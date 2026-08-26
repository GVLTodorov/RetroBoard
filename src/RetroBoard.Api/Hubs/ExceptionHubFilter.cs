using Microsoft.AspNetCore.SignalR;

namespace RetroBoard.Api.Hubs;

/// <summary>
/// Translates the built-in exception types Board.cs throws for expected board-rule violations
/// (invalid column/card id, an action attempted in the wrong phase, a non-facilitator trying to
/// reveal/advance/remove/convert, voting past budget, ...) into a client-visible
/// <see cref="HubException"/>. <see cref="HubException"/> messages always reach the client, even
/// without <c>EnableDetailedErrors</c>, so this is the one exception type worth surfacing verbatim.
///
/// These are the same exception types the BCL/ASP.NET Core throw for unrelated internal errors, so
/// catching them broadly here does mean an unrelated bug inside a hub method would also get its raw
/// message forwarded to the client instead of failing as an unhandled exception. Board.cs's hub
/// methods are thin wrappers around it with no other realistic throw sources, which keeps that risk
/// low in this app -- a deliberate tradeoff for not maintaining a separate custom exception hierarchy.
/// </summary>
public sealed class ExceptionHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or KeyNotFoundException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new HubException(ex.Message);
        }
    }
}
