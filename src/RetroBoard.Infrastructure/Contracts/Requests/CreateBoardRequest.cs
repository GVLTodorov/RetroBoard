namespace RetroBoard.Contracts.Requests;

public sealed record CreateBoardRequest(
    string Name,
    TemplateType Template,
    bool BlurUntilReveal,
    int? VoteBudget,
    int? MaxVotesPerCard);
