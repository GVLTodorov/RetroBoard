using Microsoft.AspNetCore.Components;

namespace RetroBoard.Client.Services;

public sealed class BoardHubClientFactory : IBoardHubClientFactory
{
    private readonly NavigationManager _navigation;

    public BoardHubClientFactory(NavigationManager navigation)
    {
        _navigation = navigation;
    }

    public IBoardHubClient Create() => new BoardHubClient(_navigation);
}

/// <summary>1 production implementer -- colocated with it per the repo's interface convention.</summary>
public interface IBoardHubClientFactory
{
    IBoardHubClient Create();
}
