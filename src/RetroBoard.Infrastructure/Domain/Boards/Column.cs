namespace RetroBoard.Domain.Boards;

/// <summary>
/// One column of a board, seeded from <see cref="Templates.TemplateCatalog"/> at board creation and
/// fixed thereafter. <see cref="IsRevealed"/> only matters when the owning board's
/// <see cref="Board.BlurUntilReveal"/> is set — otherwise every column starts (and stays) revealed.
/// </summary>
public sealed class Column
{
    private readonly List<Card> _cards = [];

    public Guid Id { get; }
    public string Title { get; }
    public bool IsRevealed { get; internal set; }
    public IReadOnlyList<Card> Cards => _cards;

    internal Column(Guid id, string title, bool isRevealed)
    {
        Id = id;
        Title = title;
        IsRevealed = isRevealed;
    }

    internal void AddCard(Card card) => _cards.Add(card);

    internal bool RemoveCard(Guid cardId) => _cards.RemoveAll(c => c.Id == cardId) > 0;

    internal Card? FindCard(Guid cardId) => _cards.FirstOrDefault(c => c.Id == cardId);
}
