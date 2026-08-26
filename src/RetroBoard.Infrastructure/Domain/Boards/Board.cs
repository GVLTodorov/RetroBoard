using RetroBoard.Domain.Templates;

namespace RetroBoard.Domain.Boards;

/// <summary>
/// Aggregate root for a single retro board. All mutations are synchronized with a plain lock: these
/// are pure in-memory operations (not I/O), but concurrent hub calls from different participants can
/// race on the same board's participant/column state.
/// </summary>
public sealed class Board
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, Participant> _participants = [];
    private readonly List<Column> _columns;
    private readonly List<ActionItem> _actionItems = [];

    public BoardId Id { get; }
    public string Name { get; }
    public TemplateType Template { get; }
    public bool BlurUntilReveal { get; }
    public int VoteBudget { get; }
    public int MaxVotesPerCard { get; }
    public BoardPhase Phase { get; private set; } = BoardPhase.Writing;
    public bool VotesRevealed { get; private set; }
    public DateTime? TimerEndsAtUtc { get; private set; }

    public Board(BoardId id, string name, TemplateType template, bool blurUntilReveal, int voteBudget, int maxVotesPerCard)
    {
        Id = id;
        Name = name;
        Template = template;
        BlurUntilReveal = blurUntilReveal;
        VoteBudget = voteBudget;
        MaxVotesPerCard = maxVotesPerCard;
        _columns = TemplateCatalog.Get(template)
            .Select(title => new Column(Guid.NewGuid(), title, isRevealed: !blurUntilReveal))
            .ToList();
    }

    /// <summary>
    /// The first participant to join an empty board becomes its facilitator. If
    /// <paramref name="existingParticipantId"/> still identifies a participant currently on this
    /// board — a reconnect (e.g. a page refresh) whose old connection hasn't been swept yet — that
    /// participant's identity is reused instead of minting a new one, so a reconnecting facilitator
    /// doesn't lose facilitator status.
    /// </summary>
    public Participant AddParticipant(string name, Guid? existingParticipantId = null)
    {
        lock (_lock)
        {
            if (existingParticipantId is { } id && _participants.TryGetValue(id, out var existing))
            {
                existing.Name = name;
                return existing;
            }

            var isFacilitator = _participants.Count == 0;
            var participant = new Participant(existingParticipantId ?? Guid.NewGuid(), name, isFacilitator);
            _participants[participant.Id] = participant;
            return participant;
        }
    }

    /// <returns>True if the board has no participants left, so the caller should delete it entirely.</returns>
    public bool RemoveParticipant(Guid participantId)
    {
        lock (_lock)
        {
            _participants.Remove(participantId);
            return _participants.Count == 0;
        }
    }

    /// <summary>Only the facilitator may remove another participant, enforced here, not just as a
    /// hidden button.</summary>
    public void RemoveParticipantAsFacilitator(Guid facilitatorId, Guid targetParticipantId)
    {
        lock (_lock)
        {
            RequireFacilitator(facilitatorId);
            GetParticipantCore(targetParticipantId);
            _participants.Remove(targetParticipantId);
        }
    }

    public Participant GetParticipant(Guid participantId)
    {
        lock (_lock)
        {
            return GetParticipantCore(participantId);
        }
    }

    public bool TryGetFacilitatorId(out Guid facilitatorId)
    {
        lock (_lock)
        {
            var facilitator = _participants.Values.FirstOrDefault(p => p.IsFacilitator);
            facilitatorId = facilitator?.Id ?? Guid.Empty;
            return facilitator is not null;
        }
    }

    public bool IsEmpty
    {
        get
        {
            lock (_lock)
            {
                return _participants.Count == 0;
            }
        }
    }

    public Card AddCard(Guid participantId, Guid columnId, string text)
    {
        lock (_lock)
        {
            GetParticipantCore(participantId);
            var column = GetColumnCore(columnId);

            var author = GetParticipantCore(participantId);
            var card = new Card(Guid.NewGuid(), columnId, participantId, author.Name, text);
            column.AddCard(card);
            return card;
        }
    }

    /// <summary>Only the facilitator may delete a card, enforced here, not just as a hidden button.
    /// Deleting a stack root also removes whatever is merged onto it.</summary>
    public void DeleteCard(Guid facilitatorId, Guid columnId, Guid cardId)
    {
        lock (_lock)
        {
            RequireFacilitator(facilitatorId);
            var column = GetColumnCore(columnId);
            if (!column.RemoveCard(cardId))
            {
                throw new KeyNotFoundException($"Card '{cardId}' is not in column '{columnId}'.");
            }
        }
    }

    /// <summary>
    /// Any participant can merge one top-level card onto another to consolidate duplicate/similar
    /// ideas — but only while still writing (once voting starts, cards already carry votes that a
    /// merge would orphan) and only between cards the caller can actually see (their own, or a card
    /// in an already-revealed column) — never a card hidden from them under blur-until-reveal.
    /// </summary>
    public void MergeCard(Guid participantId, Guid columnId, Guid sourceCardId, Guid targetCardId)
    {
        lock (_lock)
        {
            GetParticipantCore(participantId);

            if (Phase != BoardPhase.Writing)
            {
                throw new InvalidOperationException("Cards can only be merged during the writing phase.");
            }

            if (sourceCardId == targetCardId)
            {
                throw new InvalidOperationException("A card cannot be merged onto itself.");
            }

            var column = GetColumnCore(columnId);
            var source = column.FindCard(sourceCardId)
                ?? throw new KeyNotFoundException($"Card '{sourceCardId}' is not in column '{columnId}'.");
            var target = column.FindCard(targetCardId)
                ?? throw new KeyNotFoundException($"Card '{targetCardId}' is not in column '{columnId}'.");

            if (!CanSeeCard(column, source, participantId) || !CanSeeCard(column, target, participantId))
            {
                throw new UnauthorizedAccessException("You can only merge cards you can currently see.");
            }

            column.RemoveCard(sourceCardId);
            target.Stack(source);
        }
    }

    /// <summary>Only the facilitator may reveal a column's cards, enforced here, not just as a
    /// disabled button. A no-op once already revealed.</summary>
    public void RevealColumn(Guid facilitatorId, Guid columnId)
    {
        lock (_lock)
        {
            RequireFacilitator(facilitatorId);
            GetColumnCore(columnId).IsRevealed = true;
        }
    }

    /// <summary>Only the facilitator may advance the board, enforced here, not just as a disabled
    /// button. Writing -> Voting is always allowed; Voting -> ActionItems requires
    /// <see cref="EndVoting"/> to have run first, so vote counts are never skipped straight to the
    /// action-items phase unrevealed.</summary>
    public void AdvancePhase(Guid facilitatorId)
    {
        lock (_lock)
        {
            RequireFacilitator(facilitatorId);

            switch (Phase)
            {
                case BoardPhase.Writing:
                    Phase = BoardPhase.Voting;
                    break;
                case BoardPhase.Voting when VotesRevealed:
                    Phase = BoardPhase.ActionItems;
                    break;
                case BoardPhase.Voting:
                    throw new InvalidOperationException("End voting before advancing to the action-items phase.");
                default:
                    throw new InvalidOperationException("The board is already in its final phase.");
            }
        }
    }

    /// <summary>Only the facilitator may end voting, enforced here, not just as a disabled button.
    /// Reveals every card's vote count at once — hidden individually until now.</summary>
    public void EndVoting(Guid facilitatorId)
    {
        lock (_lock)
        {
            RequireFacilitator(facilitatorId);

            if (Phase != BoardPhase.Voting)
            {
                throw new InvalidOperationException("Voting can only be ended during the voting phase.");
            }

            VotesRevealed = true;
        }
    }

    /// <summary>Sets (not increments) a participant's vote allocation on a top-level card, enforcing
    /// the per-card cap and the participant's total vote budget across the whole board.</summary>
    public void CastVote(Guid participantId, Guid cardId, int voteCount)
    {
        lock (_lock)
        {
            GetParticipantCore(participantId);

            if (Phase != BoardPhase.Voting)
            {
                throw new InvalidOperationException("Votes can only be cast during the voting phase.");
            }

            if (voteCount < 0 || voteCount > MaxVotesPerCard)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(voteCount), voteCount, $"A single card can receive at most {MaxVotesPerCard} votes.");
            }

            var card = FindTopLevelCardCore(cardId);
            var totalExcludingThisCard = TotalVotesCastByCore(participantId) - card.VotesFor(participantId);
            if (totalExcludingThisCard + voteCount > VoteBudget)
            {
                throw new InvalidOperationException($"Vote budget of {VoteBudget} exceeded.");
            }

            card.SetVotes(participantId, voteCount);
        }
    }

    /// <summary>Only the facilitator may convert a card into a tracked action item, enforced here,
    /// not just as a hidden button, and only once the board has reached the action-items phase.</summary>
    public ActionItem ConvertToActionItem(Guid facilitatorId, Guid cardId, string? assigneeName, DateOnly? dueDate)
    {
        lock (_lock)
        {
            RequireFacilitator(facilitatorId);

            if (Phase != BoardPhase.ActionItems)
            {
                throw new InvalidOperationException("Cards can only be converted to action items in the action-items phase.");
            }

            var card = FindTopLevelCardCore(cardId);
            var actionItem = new ActionItem(Guid.NewGuid(), card.Text, card.Id, assigneeName, dueDate);
            _actionItems.Add(actionItem);
            return actionItem;
        }
    }

    /// <summary>Only the facilitator may start the timer, enforced here, not just as a disabled
    /// button. Hands out one fixed end timestamp — every client renders its own countdown from it,
    /// so there's no per-tick server push and no per-client drift.</summary>
    public void StartTimer(Guid facilitatorId, int seconds)
    {
        lock (_lock)
        {
            RequireFacilitator(facilitatorId);

            if (seconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "Timer duration must be positive.");
            }

            TimerEndsAtUtc = DateTime.UtcNow.AddSeconds(seconds);
        }
    }

    /// <summary>Only the facilitator may stop the timer, enforced here, not just as a disabled
    /// button.</summary>
    public void StopTimer(Guid facilitatorId)
    {
        lock (_lock)
        {
            RequireFacilitator(facilitatorId);
            TimerEndsAtUtc = null;
        }
    }

    /// <summary>Builds a snapshot of this board as <paramref name="viewerId"/> is currently allowed
    /// to see it — see <see cref="BoardView"/> and <see cref="ColumnView"/> for exactly what's
    /// hidden and why.</summary>
    public BoardView GetState(Guid viewerId)
    {
        lock (_lock)
        {
            var participants = _participants.Values
                .Select(p => new ParticipantView(p.Id, p.Name, p.IsFacilitator))
                .ToList();

            var columns = _columns.Select(column => BuildColumnView(column, viewerId)).ToList();

            return new BoardView(
                Id, Name, Template, Phase, VotesRevealed, BlurUntilReveal, VoteBudget, MaxVotesPerCard, TimerEndsAtUtc,
                participants, columns, _actionItems.ToList());
        }
    }

    private ColumnView BuildColumnView(Column column, Guid viewerId)
    {
        var contentVisible = column.IsRevealed || !BlurUntilReveal;
        var visible = new List<CardView>();
        var hiddenCounts = new Dictionary<string, int>();

        foreach (var card in column.Cards)
        {
            if (contentVisible || card.AuthorId == viewerId)
            {
                visible.Add(BuildCardView(card, viewerId));
            }
            else
            {
                hiddenCounts[card.AuthorName] = hiddenCounts.GetValueOrDefault(card.AuthorName) + 1;
            }
        }

        return new ColumnView(
            column.Id, column.Title, column.IsRevealed, visible,
            hiddenCounts.Select(kv => new AuthorCardCount(kv.Key, kv.Value)).ToList());
    }

    private CardView BuildCardView(Card card, Guid viewerId)
    {
        var votesVisible = VotesRevealed || Phase == BoardPhase.ActionItems;
        return new CardView(
            card.Id, card.Text, card.AuthorId, card.AuthorName,
            votesVisible ? card.TotalVotes : null,
            card.VotesFor(viewerId),
            card.StackedCards.Select(c => BuildCardView(c, viewerId)).ToList());
    }

    private bool CanSeeCard(Column column, Card card, Guid participantId) =>
        !BlurUntilReveal || column.IsRevealed || card.AuthorId == participantId;

    private int TotalVotesCastByCore(Guid participantId) =>
        _columns.SelectMany(c => c.Cards).Sum(card => card.VotesFor(participantId));

    private Card FindTopLevelCardCore(Guid cardId) =>
        _columns.SelectMany(c => c.Cards).FirstOrDefault(c => c.Id == cardId)
            ?? throw new KeyNotFoundException($"Card '{cardId}' was not found.");

    private void RequireFacilitator(Guid participantId)
    {
        if (!GetParticipantCore(participantId).IsFacilitator)
        {
            throw new UnauthorizedAccessException("Only the board facilitator can perform this action.");
        }
    }

    private Column GetColumnCore(Guid columnId) =>
        _columns.FirstOrDefault(c => c.Id == columnId)
            ?? throw new KeyNotFoundException($"Column '{columnId}' was not found.");

    private Participant GetParticipantCore(Guid participantId)
    {
        if (!_participants.TryGetValue(participantId, out var participant))
        {
            throw new KeyNotFoundException($"Participant '{participantId}' is not on this board.");
        }

        return participant;
    }
}
