namespace RetroBoard.Contracts;

public sealed record BoardSummaryResponse(string BoardId, string Name, TemplateType Template);
