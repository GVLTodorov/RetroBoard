using Microsoft.Playwright;

namespace RetroBoard.Tests.Play;

/// <summary>
/// Drives one simulated participant's browser session through the real Home/Join/Board UI (never
/// the hub or REST API directly) so a recording of it looks exactly like what a real user would
/// see. Kept independent of the specific demo scenario in Program.cs so it can be reused for other
/// browser-driven scenarios later -- mirrors PlanningPoker.Tests.Play's DemoPlayer.
/// </summary>
public sealed class RetroBoardPlayer(IPage page)
{
    public IPage Page { get; } = page;

    /// <summary>
    /// Creates a fresh board via the Home screen and joins it -- since the board is empty, this
    /// player becomes facilitator (first joiner wins, enforced server-side). Leaves the
    /// server-suggested board name as-is; only waits for it to actually arrive before submitting,
    /// since the create button silently no-ops (with a validation error) on an empty name.
    /// </summary>
    public async Task<string> CreateBoardAndJoinAsFacilitatorAsync(string baseUrl, string participantName, bool blurUntilReveal)
    {
        await Page.GotoAsync(baseUrl);
        await WaitForNonEmptyValueAsync(Page.GetByLabel("Board name"));

        await Page.GetByLabel("Your name").FillAsync(participantName);

        if (blurUntilReveal)
        {
            await Page.GetByLabel("Blur cards until each column is revealed").CheckAsync();
        }

        await Task.Delay(2_000); // lingers on the filled-in form so the recording shows the options

        var createBoardButton = Page.GetByRole(AriaRole.Button, new() { Name = "Create Board" });
        await createBoardButton.ScrollIntoViewIfNeededAsync();
        await Task.Delay(1_000);
        await createBoardButton.ClickAsync();
        await Page.Locator(".participant-list").WaitForAsync();

        return BoardIdFromUrl();
    }

    /// <summary>Joins an existing board via the Join screen. Every subsequent joiner is a guest.</summary>
    public async Task JoinBoardAsync(string baseUrl, string boardId, string participantName)
    {
        await Page.GotoAsync($"{baseUrl}/{boardId}/join");
        await Page.GetByLabel("Your name").WaitForAsync();

        await Page.GetByLabel("Your name").FillAsync(participantName);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Join Board" }).ClickAsync();
        await Page.Locator(".participant-list").WaitForAsync();
    }

    /// <summary>Adds a card with the given text to the nth column (0-indexed, left to right).</summary>
    public async Task AddCardAsync(int columnIndex, string text)
    {
        var column = Page.Locator(".board-column").Nth(columnIndex);
        await column.GetByPlaceholder("Add a card…").FillAsync(text);
        await column.GetByRole(AriaRole.Button, new() { Name = "Add card" }).ClickAsync();
    }

    /// <summary>
    /// Clicks every column's "Reveal" button one at a time (only rendered when the board was
    /// created with blur-until-reveal, and disappears from a column once that column is revealed)
    /// with a short pause between clicks so each column's cards visibly un-blur in sequence.
    /// </summary>
    public async Task RevealAllColumnsAsync()
    {
        var revealButton = Page.GetByRole(AriaRole.Button, new() { Name = "Reveal" });
        while (await revealButton.CountAsync() > 0)
        {
            await revealButton.First.ClickAsync();
            await Task.Delay(1_000);
        }
    }

    public Task StartVotingAsync() =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Start Voting" }).ClickAsync();

    public Task EndVotingAsync() =>
        Page.GetByRole(AriaRole.Button, new() { Name = "End Voting" }).ClickAsync();

    public Task AdvanceToActionItemsAsync() =>
        Page.GetByRole(AriaRole.Button, new() { Name = "Advance to Action Items" }).ClickAsync();

    /// <summary>Adds one vote to the nth card (0-indexed) of the nth column, if that card can still
    /// take a vote (per-card cap or budget exhausted just skips it, rather than throwing).</summary>
    public async Task AddVoteAsync(int columnIndex, int cardIndex)
    {
        var card = Page.Locator(".board-column").Nth(columnIndex).Locator(".card").Nth(cardIndex);
        var addVoteButton = card.GetByRole(AriaRole.Button, new() { Name = "Add a vote" });

        if (await addVoteButton.IsEnabledAsync())
        {
            await addVoteButton.ClickAsync();
        }
    }

    /// <summary>Converts the nth card (0-indexed) of the nth column into an action item, during the
    /// Action Items phase, with an optional assignee.</summary>
    public async Task ConvertToActionItemAsync(int columnIndex, int cardIndex, string? assigneeName)
    {
        var card = Page.Locator(".board-column").Nth(columnIndex).Locator(".card").Nth(cardIndex);
        await card.GetByRole(AriaRole.Button, new() { Name = "Convert to action item" }).ClickAsync();

        if (assigneeName is not null)
        {
            await card.GetByPlaceholder("Assignee (optional)").FillAsync(assigneeName);
        }

        await card.GetByRole(AriaRole.Button, new() { Name = "Convert", Exact = true }).ClickAsync();
    }

    private static async Task WaitForNonEmptyValueAsync(ILocator locator)
    {
        await Assertions.Expect(locator).Not.ToHaveValueAsync(string.Empty, new() { Timeout = 30_000 });
    }

    private string BoardIdFromUrl()
    {
        var uri = new Uri(Page.Url);
        return uri.AbsolutePath.Trim('/');
    }
}
