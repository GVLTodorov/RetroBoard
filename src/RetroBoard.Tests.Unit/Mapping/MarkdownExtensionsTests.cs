using RetroBoard.Api.Extensions;
using RetroBoard.Contracts;

namespace RetroBoard.Tests.Unit.Mapping;

public class MarkdownExtensionsTests
{
    [Fact]
    public void ToMarkdown_IncludesColumnsCardsAndActionItems()
    {
        var card = new CardResponse(Guid.NewGuid(), "Ship it", Guid.NewGuid(), "Alice", 3, 0, []);
        var column = new ColumnResponse(Guid.NewGuid(), "Went well", true, [card], []);
        var actionItem = new ActionItemResponse(
            Guid.NewGuid(), "Automate deploys", card.CardId, "Bob", new DateOnly(2026, 9, 1));
        var state = new BoardStateResponse(
            "sprint-retro", "Sprint Retro", TemplateType.WentWellDidntWork, BoardPhase.ActionItems, true, false,
            5, 3, null, [], [column], [actionItem]);

        var markdown = state.ToMarkdown();

        Assert.Contains("# Sprint Retro", markdown);
        Assert.Contains("## Went well", markdown);
        Assert.Contains("Ship it (Alice) — 3 votes", markdown);
        Assert.Contains("## Action Items", markdown);
        Assert.Contains("Automate deploys — Bob (due 2026-09-01)", markdown);
    }

    [Fact]
    public void ToMarkdown_NotesEmptyColumnsAndActionItems()
    {
        var column = new ColumnResponse(Guid.NewGuid(), "Went well", true, [], []);
        var state = new BoardStateResponse(
            "sprint-retro", "Sprint Retro", TemplateType.WentWellDidntWork, BoardPhase.Writing, false, false,
            5, 3, null, [], [column], []);

        var markdown = state.ToMarkdown();

        Assert.Contains("_No cards._", markdown);
        Assert.Contains("_No action items._", markdown);
    }
}
