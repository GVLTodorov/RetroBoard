using RetroBoard.Api.Extensions;
using RetroBoard.Domain.Boards;
using RetroBoard.Domain.Templates;
using ContractBoardPhase = RetroBoard.Contracts.BoardPhase;
using ContractTemplateType = RetroBoard.Contracts.TemplateType;

namespace RetroBoard.Tests.Unit.Mapping;

public class ContractExtensionsTests
{
    [Fact]
    public void ToStateResponse_MapsEveryField()
    {
        BoardId.TryParse("sprint-retro", out var boardId);
        var board = new Board(boardId, "Sprint Retro", TemplateType.WentWellDidntWork, true, 5, 3);
        var alice = board.AddParticipant("Alice");
        var columnId = board.GetState(alice.Id).Columns[0].ColumnId;
        board.AddCard(alice.Id, columnId, "Ship it");

        var response = board.GetState(alice.Id).ToStateResponse();

        Assert.Equal("sprint-retro", response.BoardId);
        Assert.Equal("Sprint Retro", response.Name);
        Assert.Equal(ContractTemplateType.WentWellDidntWork, response.Template);
        Assert.Equal(ContractBoardPhase.Writing, response.Phase);
        Assert.True(response.BlurUntilReveal);
        Assert.Equal(5, response.VoteBudget);
        Assert.Equal(3, response.MaxVotesPerCard);
        Assert.Single(response.Participants);
        Assert.Equal(3, response.Columns.Count);
        Assert.Single(response.Columns[0].Cards);
        Assert.Equal("Ship it", response.Columns[0].Cards[0].Text);
    }

    [Theory]
    [InlineData(TemplateType.WentWellDidntWork, ContractTemplateType.WentWellDidntWork)]
    [InlineData(TemplateType.StartStopContinue, ContractTemplateType.StartStopContinue)]
    [InlineData(TemplateType.MadSadGlad, ContractTemplateType.MadSadGlad)]
    [InlineData(TemplateType.FourLs, ContractTemplateType.FourLs)]
    public void TemplateType_MapsBothWays(TemplateType domain, ContractTemplateType contract)
    {
        Assert.Equal(contract, domain.ToTemplateType());
        Assert.Equal(domain, contract.ToDomain());
    }
}
