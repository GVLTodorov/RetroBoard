using RetroBoard.Contracts;
using DomainBoard = RetroBoard.Domain.Boards.Board;
using DomainBoardPhase = RetroBoard.Domain.Boards.BoardPhase;
using DomainBoardView = RetroBoard.Domain.Boards.BoardView;
using DomainCardView = RetroBoard.Domain.Boards.CardView;
using DomainColumnView = RetroBoard.Domain.Boards.ColumnView;
using DomainAuthorCardCount = RetroBoard.Domain.Boards.AuthorCardCount;
using DomainParticipantView = RetroBoard.Domain.Boards.ParticipantView;
using DomainActionItem = RetroBoard.Domain.Boards.ActionItem;
using DomainTemplateType = RetroBoard.Domain.Templates.TemplateType;

namespace RetroBoard.Api.Extensions;

public static class ContractExtensions
{
    public static BoardSummaryResponse ToSummaryResponse(this DomainBoard board) =>
        new(board.Id.Value, board.Name, board.Template.ToTemplateType());

    public static BoardStateResponse ToStateResponse(this DomainBoardView view) => new(
        view.BoardId.Value,
        view.Name,
        view.Template.ToTemplateType(),
        view.Phase.ToBoardPhase(),
        view.VotesRevealed,
        view.BlurUntilReveal,
        view.VoteBudget,
        view.MaxVotesPerCard,
        view.TimerEndsAtUtc,
        view.Participants.Select(p => p.ToParticipantResponse()).ToList(),
        view.Columns.Select(c => c.ToColumnResponse()).ToList(),
        view.ActionItems.Select(a => a.ToActionItemResponse()).ToList());

    public static ParticipantResponse ToParticipantResponse(this DomainParticipantView view) =>
        new(view.ParticipantId, view.Name, view.IsFacilitator);

    public static ColumnResponse ToColumnResponse(this DomainColumnView view) => new(
        view.ColumnId,
        view.Title,
        view.IsRevealed,
        view.VisibleCards.Select(c => c.ToCardResponse()).ToList(),
        view.HiddenCardCounts.Select(h => h.ToAuthorCardCountResponse()).ToList());

    public static CardResponse ToCardResponse(this DomainCardView view) => new(
        view.CardId,
        view.Text,
        view.AuthorId,
        view.AuthorName,
        view.VoteCount,
        view.MyVoteCount,
        view.StackedCards.Select(c => c.ToCardResponse()).ToList());

    public static AuthorCardCountResponse ToAuthorCardCountResponse(this DomainAuthorCardCount count) =>
        new(count.AuthorName, count.Count);

    public static ActionItemResponse ToActionItemResponse(this DomainActionItem item) =>
        new(item.Id, item.Text, item.SourceCardId, item.AssigneeName, item.DueDate);

    public static TemplateType ToTemplateType(this DomainTemplateType type) => type switch
    {
        DomainTemplateType.WentWellDidntWork => TemplateType.WentWellDidntWork,
        DomainTemplateType.StartStopContinue => TemplateType.StartStopContinue,
        DomainTemplateType.MadSadGlad => TemplateType.MadSadGlad,
        DomainTemplateType.FourLs => TemplateType.FourLs,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown template type."),
    };

    public static DomainTemplateType ToDomain(this TemplateType type) => type switch
    {
        TemplateType.WentWellDidntWork => DomainTemplateType.WentWellDidntWork,
        TemplateType.StartStopContinue => DomainTemplateType.StartStopContinue,
        TemplateType.MadSadGlad => DomainTemplateType.MadSadGlad,
        TemplateType.FourLs => DomainTemplateType.FourLs,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown template type."),
    };

    public static BoardPhase ToBoardPhase(this DomainBoardPhase phase) => phase switch
    {
        DomainBoardPhase.Writing => BoardPhase.Writing,
        DomainBoardPhase.Voting => BoardPhase.Voting,
        DomainBoardPhase.ActionItems => BoardPhase.ActionItems,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown board phase."),
    };
}
