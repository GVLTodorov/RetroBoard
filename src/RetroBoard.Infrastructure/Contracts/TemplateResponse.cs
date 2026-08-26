namespace RetroBoard.Contracts;

public sealed record TemplateResponse(TemplateType Template, string DisplayName, IReadOnlyList<string> ColumnTitles);
