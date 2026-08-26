using Bunit;
using Xunit;
using Timer = RetroBoard.Client.Components.Timer;

namespace RetroBoard.Tests.Component;

public class TimerTests : BunitContext
{
    [Fact]
    public void ShowsPlaceholder_WhenNoTimerIsRunning()
    {
        var cut = Render<Timer>(p => p.Add(x => x.TimerEndsAtUtc, (DateTime?)null));

        Assert.Equal("--:--", cut.Find(".timer-value").TextContent);
    }

    [Fact]
    public void StartStopControls_OnlyRendered_ForFacilitator()
    {
        var facilitatorCut = Render<Timer>(p => p.Add(x => x.IsFacilitator, true));
        Assert.Single(facilitatorCut.FindAll(".timer-controls"));

        var participantCut = Render<Timer>(p => p.Add(x => x.IsFacilitator, false));
        Assert.Empty(participantCut.FindAll(".timer-controls"));
    }

    [Fact]
    public void StopButton_IsDisabled_WhenNoTimerIsRunning()
    {
        var cut = Render<Timer>(p => p.Add(x => x.IsFacilitator, true).Add(x => x.TimerEndsAtUtc, (DateTime?)null));

        var stopButton = cut.FindAll("button").Single(b => b.TextContent == "Stop");
        Assert.True(stopButton.HasAttribute("disabled"));
    }

    [Fact]
    public void StartButton_Click_RaisesOnStart_WithTheEnteredDuration()
    {
        int? raised = null;
        var cut = Render<Timer>(p => p.Add(x => x.IsFacilitator, true).Add(x => x.OnStart, v => raised = v));

        cut.FindAll("button").Single(b => b.TextContent == "Start").Click();

        Assert.Equal(RetroBoard.Contracts.Constants.DefaultTimerSeconds, raised);
    }
}
