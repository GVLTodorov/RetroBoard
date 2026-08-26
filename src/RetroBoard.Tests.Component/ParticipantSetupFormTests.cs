using Bunit;
using RetroBoard.Client.Components;
using Xunit;

namespace RetroBoard.Tests.Component;

public class ParticipantSetupFormTests : BunitContext
{
    [Fact]
    public void RendersTheSuppliedName()
    {
        var cut = Render<ParticipantSetupForm>(p => p.Add(x => x.Name, "Alice"));

        Assert.Equal("Alice", cut.Find("input").GetAttribute("value"));
    }

    [Fact]
    public void TypingIntoTheInput_RaisesNameChanged()
    {
        string? raised = null;
        var cut = Render<ParticipantSetupForm>(p => p.Add(x => x.NameChanged, v => raised = v));

        cut.Find("input").Input("Bob");

        Assert.Equal("Bob", raised);
    }
}
