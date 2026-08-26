using Bunit;
using RetroBoard.Client.Components;
using Xunit;

namespace RetroBoard.Tests.Component;

public class VoteControlsTests : BunitContext
{
    [Fact]
    public void MinusButton_IsDisabled_WhenMyVoteCountIsZero()
    {
        var cut = Render<VoteControls>(p => p.Add(x => x.MyVoteCount, 0));

        var minus = cut.FindAll("button")[0];
        Assert.True(minus.HasAttribute("disabled"));
    }

    [Fact]
    public void PlusButton_IsDisabled_AtMaxVotesPerCard()
    {
        var cut = Render<VoteControls>(p => p
            .Add(x => x.MyVoteCount, 3)
            .Add(x => x.MaxVotesPerCard, 3)
            .Add(x => x.MyRemainingVoteBudgetElsewhere, 5));

        var plus = cut.FindAll("button")[1];
        Assert.True(plus.HasAttribute("disabled"));
    }

    [Fact]
    public void PlusButton_IsDisabled_WhenNoRemainingBudget()
    {
        var cut = Render<VoteControls>(p => p
            .Add(x => x.MyVoteCount, 1)
            .Add(x => x.MaxVotesPerCard, 3)
            .Add(x => x.MyRemainingVoteBudgetElsewhere, 1));

        var plus = cut.FindAll("button")[1];
        Assert.False(plus.HasAttribute("disabled"));

        cut.Render(p => p
            .Add(x => x.MyVoteCount, 1)
            .Add(x => x.MaxVotesPerCard, 3)
            .Add(x => x.MyRemainingVoteBudgetElsewhere, 0));

        plus = cut.FindAll("button")[1];
        Assert.True(plus.HasAttribute("disabled"));
    }

    [Fact]
    public void ClickingPlus_RaisesOnChanged_WithIncrementedCount()
    {
        int? raised = null;
        var cut = Render<VoteControls>(p => p
            .Add(x => x.MyVoteCount, 1)
            .Add(x => x.MaxVotesPerCard, 3)
            .Add(x => x.MyRemainingVoteBudgetElsewhere, 5)
            .Add(x => x.OnChanged, v => raised = v));

        cut.FindAll("button")[1].Click();

        Assert.Equal(2, raised);
    }

    [Fact]
    public void ClickingMinus_RaisesOnChanged_WithDecrementedCount()
    {
        int? raised = null;
        var cut = Render<VoteControls>(p => p
            .Add(x => x.MyVoteCount, 2)
            .Add(x => x.OnChanged, v => raised = v));

        cut.FindAll("button")[0].Click();

        Assert.Equal(1, raised);
    }
}
