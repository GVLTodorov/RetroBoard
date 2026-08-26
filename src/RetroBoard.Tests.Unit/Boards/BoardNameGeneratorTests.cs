using RetroBoard.Domain.Boards;

namespace RetroBoard.Tests.Unit.Boards;

public class BoardNameGeneratorTests
{
    [Fact]
    public void Generate_ProducesAWordHyphenIdShape()
    {
        var name = BoardNameGenerator.Generate();

        var parts = name.Split('-');
        Assert.Equal(2, parts.Length);
        Assert.NotEmpty(parts[0]);
        Assert.Equal(6, parts[1].Length);
        Assert.All(parts[1], ch => Assert.True(char.IsLower(ch) || char.IsDigit(ch)));
    }

    [Fact]
    public void Generate_ProducesABoardIdParseableValue()
    {
        var name = BoardNameGenerator.Generate();

        var success = BoardId.TryParse(name, out var id);

        Assert.True(success);
        Assert.Equal(name, id.Value);
    }
}
