using RetroBoard.Api.Extensions;
using RetroBoard.Contracts;
using RetroBoard.Contracts.Requests;
using RetroBoard.Domain.Boards;
using RetroBoard.Domain.Templates;

namespace RetroBoard.Api.Endpoints;

public static class BoardEndpoints
{
    public static void MapBoardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api");

        group.MapGet("/boards/name-suggestion", () =>
            Results.Ok(new BoardNameSuggestionResponse(BoardNameGenerator.Generate())));

        group.MapPost("/boards", (CreateBoardRequest request, IBoardRepository boards) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Board name is required.");
            }

            var voteBudget = request.VoteBudget is > 0 ? request.VoteBudget.Value : Constants.DefaultVoteBudget;
            var maxVotesPerCard = request.MaxVotesPerCard is > 0
                ? request.MaxVotesPerCard.Value
                : Constants.DefaultMaxVotesPerCard;

            var board = boards.Create(
                request.Name.Trim(), request.Template.ToDomain(), request.BlurUntilReveal, voteBudget, maxVotesPerCard);
            if (board is null)
            {
                return Results.Conflict(
                    "That board name is already taken or has no usable characters for a board link. Try a different name.");
            }

            return Results.Created($"/api/boards/{board.Id.Value}", board.ToSummaryResponse());
        });

        group.MapGet("/boards/{boardId}", (string boardId, IBoardRepository boards) =>
        {
            if (!BoardId.TryParse(boardId, out var parsed) || !boards.TryGet(parsed, out var board) || board is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(board.ToSummaryResponse());
        });

        group.MapGet("/boards/{boardId}/export", (string boardId, Guid participantId, IBoardRepository boards) =>
        {
            if (!BoardId.TryParse(boardId, out var parsed) || !boards.TryGet(parsed, out var board) || board is null)
            {
                return Results.NotFound();
            }

            if (!board.TryGetFacilitatorId(out var facilitatorId) || facilitatorId != participantId)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var markdown = board.GetState(participantId).ToStateResponse().ToMarkdown();
            return Results.Text(markdown, "text/markdown");
        });

        group.MapGet("/templates", () =>
        {
            var templates = TemplateCatalog.AllTypes
                .Select(type => new TemplateResponse(
                    type.ToTemplateType(), TemplateCatalog.GetDisplayName(type), TemplateCatalog.Get(type)))
                .ToList();

            return Results.Ok(templates);
        });

        endpoints.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
    }
}
