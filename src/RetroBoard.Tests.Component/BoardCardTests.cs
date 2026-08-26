using Bunit;
using RetroBoard.Client.Components;
using RetroBoard.Contracts;
using Xunit;

namespace RetroBoard.Tests.Component;

public class BoardCardTests : BunitContext
{
    private static CardResponse MakeCard(int? voteCount = null, int myVoteCount = 0, int stackedCount = 0) =>
        new(
            Guid.NewGuid(), "Ship it faster", Guid.NewGuid(), "Alice", voteCount, myVoteCount,
            Enumerable.Range(0, stackedCount)
                .Select(_ => new CardResponse(Guid.NewGuid(), "dup", Guid.NewGuid(), "Bob", null, 0, []))
                .ToList());

    [Fact]
    public void RendersTextAndAuthor()
    {
        var cut = Render<BoardCard>(p => p.Add(x => x.Card, MakeCard()));

        Assert.Contains("Ship it faster", cut.Find(".card-text").TextContent);
        Assert.Equal("Alice", cut.Find(".card-author").TextContent);
    }

    [Fact]
    public void VoteCount_NotRendered_WhileHidden()
    {
        var cut = Render<BoardCard>(p => p.Add(x => x.Card, MakeCard(voteCount: null)));

        Assert.Empty(cut.FindAll(".card-vote-count"));
    }

    [Fact]
    public void VoteCount_Rendered_OnceRevealed()
    {
        var cut = Render<BoardCard>(p => p.Add(x => x.Card, MakeCard(voteCount: 4)));

        Assert.Equal("4 votes", cut.Find(".card-vote-count").TextContent);
    }

    [Fact]
    public void StackedCount_RendersMergedBadge()
    {
        var cut = Render<BoardCard>(p => p.Add(x => x.Card, MakeCard(stackedCount: 2)));

        Assert.Equal("+2 merged", cut.Find(".card-stacked-count").TextContent);
    }

    [Fact]
    public void DeleteButton_OnlyRendered_ForFacilitator()
    {
        var facilitatorCut = Render<BoardCard>(p => p.Add(x => x.Card, MakeCard()).Add(x => x.IsFacilitator, true));
        Assert.Single(facilitatorCut.FindAll(".card-delete-button"));

        var participantCut = Render<BoardCard>(p => p.Add(x => x.Card, MakeCard()).Add(x => x.IsFacilitator, false));
        Assert.Empty(participantCut.FindAll(".card-delete-button"));
    }

    [Fact]
    public void VoteControls_OnlyRendered_DuringVotingPhase()
    {
        var writingCut = Render<BoardCard>(p => p.Add(x => x.Card, MakeCard()).Add(x => x.Phase, BoardPhase.Writing));
        Assert.Empty(writingCut.FindAll(".vote-controls"));

        var votingCut = Render<BoardCard>(p => p.Add(x => x.Card, MakeCard()).Add(x => x.Phase, BoardPhase.Voting));
        Assert.Single(votingCut.FindAll(".vote-controls"));
    }

    [Fact]
    public void DeleteButton_Click_RaisesOnDelete()
    {
        var raised = false;
        var cut = Render<BoardCard>(p => p
            .Add(x => x.Card, MakeCard())
            .Add(x => x.IsFacilitator, true)
            .Add(x => x.OnDelete, () => raised = true));

        cut.Find(".card-delete-button").Click();

        Assert.True(raised);
    }
}
