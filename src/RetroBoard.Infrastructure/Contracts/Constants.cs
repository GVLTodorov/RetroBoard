namespace RetroBoard.Contracts;

public static class Constants
{
    public const string BoardStateChangedEvent = "BoardStateChanged";
    public const string RemovedFromBoardEvent = "RemovedFromBoard";

    public const int DefaultVoteBudget = 5;
    public const int DefaultMaxVotesPerCard = 3;
    public const int DefaultTimerSeconds = 300;
}
