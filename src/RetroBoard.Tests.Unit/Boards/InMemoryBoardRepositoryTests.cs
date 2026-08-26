using RetroBoard.Domain.Boards;
using RetroBoard.Domain.Templates;

namespace RetroBoard.Tests.Unit.Boards;

public class InMemoryBoardRepositoryTests
{
    [Fact]
    public void Create_ThenTryGet_ReturnsTheSameBoard()
    {
        var repository = new InMemoryBoardRepository();

        var created = repository.Create("Sprint Retro", TemplateType.StartStopContinue, false, 5, 3);
        var found = repository.TryGet(created!.Id, out var board);

        Assert.True(found);
        Assert.Same(created, board);
    }

    [Fact]
    public void Create_ReturnsNull_WhenNameHasNoUsableCharacters()
    {
        var repository = new InMemoryBoardRepository();

        var created = repository.Create("!!!", TemplateType.StartStopContinue, false, 5, 3);

        Assert.Null(created);
    }

    [Fact]
    public void Create_ReturnsNull_OnSlugCollision()
    {
        var repository = new InMemoryBoardRepository();

        var first = repository.Create("Sprint Retro", TemplateType.StartStopContinue, false, 5, 3);
        var second = repository.Create("sprint retro", TemplateType.MadSadGlad, false, 5, 3);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void Remove_MakesTheBoardUnreachable()
    {
        var repository = new InMemoryBoardRepository();
        var created = repository.Create("Sprint Retro", TemplateType.StartStopContinue, false, 5, 3);

        repository.Remove(created!.Id);

        Assert.False(repository.TryGet(created.Id, out _));
    }

    [Fact]
    public void TryGet_ReturnsFalse_ForUnknownBoard()
    {
        var repository = new InMemoryBoardRepository();
        BoardId.TryParse("unknown-board", out var id);

        Assert.False(repository.TryGet(id, out var board));
        Assert.Null(board);
    }
}
