using RetroBoard.Domain.Boards;

namespace RetroBoard.Tests.Unit.Boards;

public class BoardIdTests
{
    [Theory]
    [InlineData("Sprint 12 Retro", "sprint-12-retro")]
    [InlineData("  Leading And Trailing  ", "leading-and-trailing")]
    [InlineData("Already-Slug", "already-slug")]
    [InlineData("Multiple---Hyphens", "multiple-hyphens")]
    [InlineData("Special!!Chars@@Here", "special-chars-here")]
    public void TryParse_Slugifies(string input, string expected)
    {
        var success = BoardId.TryParse(input, out var id);

        Assert.True(success);
        Assert.Equal(expected, id.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void TryParse_ReturnsFalse_WhenNoUsableCharacters(string? input)
    {
        var success = BoardId.TryParse(input, out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParse_IsIdempotent_OnAnAlreadySlugifiedValue()
    {
        BoardId.TryParse("Sprint 12 Retro", out var first);
        BoardId.TryParse(first.Value, out var second);

        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public void TryParse_TruncatesToMaxLength()
    {
        var longName = new string('a', 200);

        BoardId.TryParse(longName, out var id);

        Assert.Equal(60, id.Value.Length);
    }
}
