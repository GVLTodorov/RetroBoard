using Bunit;
using RetroBoard.Client.Components;
using RetroBoard.Contracts;
using Xunit;

namespace RetroBoard.Tests.Component;

public class TemplatePickerTests : BunitContext
{
    private static readonly List<TemplateResponse> Templates =
    [
        new(TemplateType.WentWellDidntWork, "Went well / Didn't go well / Action items", ["Went well", "Didn't go well", "Action items"]),
        new(TemplateType.StartStopContinue, "Start / Stop / Continue", ["Start", "Stop", "Continue"]),
    ];

    [Fact]
    public void RendersEachTemplateOption_WithDisplayName()
    {
        var cut = Render<TemplatePicker>(p => p
            .Add(x => x.Templates, Templates)
            .Add(x => x.SelectedTemplate, TemplateType.WentWellDidntWork));

        var options = cut.FindAll("option");

        Assert.Equal(2, options.Count);
        Assert.Equal("Went well / Didn't go well / Action items", options[0].TextContent);
        Assert.Equal("Start / Stop / Continue", options[1].TextContent);
    }

    [Fact]
    public void SelectingAnOption_RaisesSelectedTemplateChanged()
    {
        TemplateType? changedTo = null;
        var cut = Render<TemplatePicker>(p => p
            .Add(x => x.Templates, Templates)
            .Add(x => x.SelectedTemplate, TemplateType.WentWellDidntWork)
            .Add(x => x.SelectedTemplateChanged, t => changedTo = t));

        cut.Find("select").Change(nameof(TemplateType.StartStopContinue));

        Assert.Equal(TemplateType.StartStopContinue, changedTo);
    }
}
