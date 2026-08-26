using RetroBoard.Domain.Templates;

namespace RetroBoard.Tests.Unit.Templates;

public class TemplateCatalogTests
{
    [Theory]
    [InlineData(TemplateType.WentWellDidntWork, 3)]
    [InlineData(TemplateType.StartStopContinue, 3)]
    [InlineData(TemplateType.MadSadGlad, 3)]
    [InlineData(TemplateType.FourLs, 4)]
    public void Get_ReturnsExpectedColumnCount(TemplateType type, int expectedCount)
    {
        var columns = TemplateCatalog.Get(type);

        Assert.Equal(expectedCount, columns.Count);
    }

    [Fact]
    public void AllTypes_EachHaveNonEmptyColumnsAndDisplayName()
    {
        foreach (var type in TemplateCatalog.AllTypes)
        {
            Assert.NotEmpty(TemplateCatalog.Get(type));
            Assert.False(string.IsNullOrWhiteSpace(TemplateCatalog.GetDisplayName(type)));
        }
    }

    [Fact]
    public void WentWellDidntWork_HasExpectedColumnTitles()
    {
        var columns = TemplateCatalog.Get(TemplateType.WentWellDidntWork);

        Assert.Equal(["Went well", "Didn't go well", "Action items"], columns);
    }

    [Fact]
    public void FourLs_HasExpectedColumnTitles()
    {
        var columns = TemplateCatalog.Get(TemplateType.FourLs);

        Assert.Equal(["Liked", "Learned", "Lacked", "Longed for"], columns);
    }
}
