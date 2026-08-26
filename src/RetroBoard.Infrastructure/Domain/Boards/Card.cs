namespace RetroBoard.Domain.Boards;

/// <summary>
/// A single sticky note. <see cref="StackedCards"/> holds notes merged onto this one via
/// <see cref="Board.MergeCard"/> — a lightweight duplicate/similar-idea consolidation, not full text
/// editing of someone else's card. Only a top-level (non-stacked) card can be voted on or merged
/// into further; <see cref="_votesByParticipant"/> tracks each participant's own allocation on this
/// specific card so <see cref="Board"/> can enforce the per-card and per-participant budgets.
/// </summary>
public sealed class Card
{
    private readonly List<Card> _stackedCards = [];
    private readonly Dictionary<Guid, int> _votesByParticipant = [];

    public Guid Id { get; }
    public Guid ColumnId { get; }
    public Guid AuthorId { get; }
    public string AuthorName { get; internal set; }
    public string Text { get; internal set; }
    public IReadOnlyList<Card> StackedCards => _stackedCards;
    public int TotalVotes => _votesByParticipant.Values.Sum();

    internal Card(Guid id, Guid columnId, Guid authorId, string authorName, string text)
    {
        Id = id;
        ColumnId = columnId;
        AuthorId = authorId;
        AuthorName = authorName;
        Text = text;
    }

    internal void Stack(Card card) => _stackedCards.Add(card);

    internal int VotesFor(Guid participantId) => _votesByParticipant.GetValueOrDefault(participantId);

    internal void SetVotes(Guid participantId, int count)
    {
        if (count <= 0)
        {
            _votesByParticipant.Remove(participantId);
        }
        else
        {
            _votesByParticipant[participantId] = count;
        }
    }

    internal void ClearVotes() => _votesByParticipant.Clear();
}
