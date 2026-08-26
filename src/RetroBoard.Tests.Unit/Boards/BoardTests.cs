using RetroBoard.Domain.Boards;
using RetroBoard.Domain.Templates;

namespace RetroBoard.Tests.Unit.Boards;

public class BoardTests
{
    private static Board CreateBoard(
        bool blurUntilReveal = false,
        TemplateType template = TemplateType.StartStopContinue,
        int voteBudget = 5,
        int maxVotesPerCard = 3)
    {
        BoardId.TryParse("test-board", out var id);
        return new Board(id, "Test Board", template, blurUntilReveal, voteBudget, maxVotesPerCard);
    }

    private static Guid FirstColumnId(Board board) => board.GetState(Guid.NewGuid()).Columns[0].ColumnId;

    // Participants / facilitator

    [Fact]
    public void AddParticipant_FirstJoinerBecomesFacilitator()
    {
        var board = CreateBoard();

        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");

        Assert.True(alice.IsFacilitator);
        Assert.False(bob.IsFacilitator);
    }

    [Fact]
    public void AddParticipant_WithExistingId_ReusesIdentityAndKeepsFacilitatorStatus()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");

        var reconnected = board.AddParticipant("Alice Renamed", alice.Id);

        Assert.Equal(alice.Id, reconnected.Id);
        Assert.True(reconnected.IsFacilitator);
        Assert.Equal("Alice Renamed", reconnected.Name);
    }

    [Fact]
    public void RemoveParticipant_ReturnsTrue_WhenBoardBecomesEmpty()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");

        var isEmpty = board.RemoveParticipant(alice.Id);

        Assert.True(isEmpty);
        Assert.True(board.IsEmpty);
    }

    [Fact]
    public void RemoveParticipantAsFacilitator_NonFacilitator_Throws()
    {
        var board = CreateBoard();
        board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        var carol = board.AddParticipant("Carol");

        Assert.Throws<UnauthorizedAccessException>(() => board.RemoveParticipantAsFacilitator(bob.Id, carol.Id));
    }

    [Fact]
    public void RemoveParticipantAsFacilitator_ByFacilitator_RemovesTarget()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");

        board.RemoveParticipantAsFacilitator(alice.Id, bob.Id);

        Assert.DoesNotContain(board.GetState(alice.Id).Participants, p => p.ParticipantId == bob.Id);
    }

    [Fact]
    public void TryGetFacilitatorId_ReturnsFalse_WhenBoardIsEmpty()
    {
        var board = CreateBoard();

        Assert.False(board.TryGetFacilitatorId(out _));
    }

    [Fact]
    public void TryGetFacilitatorId_ReturnsFacilitator_AfterFirstJoin()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        board.AddParticipant("Bob");

        var found = board.TryGetFacilitatorId(out var facilitatorId);

        Assert.True(found);
        Assert.Equal(alice.Id, facilitatorId);
    }

    // Cards

    [Fact]
    public void AddCard_UnknownColumn_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");

        Assert.Throws<KeyNotFoundException>(() => board.AddCard(alice.Id, Guid.NewGuid(), "text"));
    }

    [Fact]
    public void AddCard_AppearsInGetState()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);

        board.AddCard(alice.Id, columnId, "Ship it");

        var column = board.GetState(alice.Id).Columns.Single(c => c.ColumnId == columnId);
        Assert.Single(column.VisibleCards);
        Assert.Equal("Ship it", column.VisibleCards[0].Text);
    }

    [Fact]
    public void DeleteCard_NonFacilitator_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        var columnId = FirstColumnId(board);
        var card = board.AddCard(alice.Id, columnId, "text");

        Assert.Throws<UnauthorizedAccessException>(() => board.DeleteCard(bob.Id, columnId, card.Id));
    }

    [Fact]
    public void DeleteCard_ByFacilitator_RemovesIt()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var card = board.AddCard(alice.Id, columnId, "text");

        board.DeleteCard(alice.Id, columnId, card.Id);

        var column = board.GetState(alice.Id).Columns.Single(c => c.ColumnId == columnId);
        Assert.Empty(column.VisibleCards);
    }

    // Merging / grouping

    [Fact]
    public void MergeCard_OutsideWritingPhase_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var a = board.AddCard(alice.Id, columnId, "a");
        var b = board.AddCard(alice.Id, columnId, "b");
        board.AdvancePhase(alice.Id);

        Assert.Throws<InvalidOperationException>(() => board.MergeCard(alice.Id, columnId, a.Id, b.Id));
    }

    [Fact]
    public void MergeCard_OntoItself_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var a = board.AddCard(alice.Id, columnId, "a");

        Assert.Throws<InvalidOperationException>(() => board.MergeCard(alice.Id, columnId, a.Id, a.Id));
    }

    [Fact]
    public void MergeCard_SucceedsForOwnCards_AndNestsUnderTarget()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var source = board.AddCard(alice.Id, columnId, "duplicate idea");
        var target = board.AddCard(alice.Id, columnId, "main idea");

        board.MergeCard(alice.Id, columnId, source.Id, target.Id);

        var column = board.GetState(alice.Id).Columns.Single(c => c.ColumnId == columnId);
        var visible = Assert.Single(column.VisibleCards);
        Assert.Equal(target.Id, visible.CardId);
        var stacked = Assert.Single(visible.StackedCards);
        Assert.Equal(source.Id, stacked.CardId);
    }

    [Fact]
    public void MergeCard_UnderBlur_CannotMergeAnotherAuthorsUnrevealedCard()
    {
        var board = CreateBoard(blurUntilReveal: true);
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        var columnId = FirstColumnId(board);
        var aliceCard = board.AddCard(alice.Id, columnId, "alice's");
        var bobCard = board.AddCard(bob.Id, columnId, "bob's");

        Assert.Throws<UnauthorizedAccessException>(() => board.MergeCard(alice.Id, columnId, bobCard.Id, aliceCard.Id));
    }

    [Fact]
    public void MergeCard_UnderBlur_AllowedOnceColumnRevealed()
    {
        var board = CreateBoard(blurUntilReveal: true);
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        var columnId = FirstColumnId(board);
        var aliceCard = board.AddCard(alice.Id, columnId, "alice's");
        var bobCard = board.AddCard(bob.Id, columnId, "bob's");
        board.RevealColumn(alice.Id, columnId);

        board.MergeCard(alice.Id, columnId, bobCard.Id, aliceCard.Id);

        var column = board.GetState(alice.Id).Columns.Single(c => c.ColumnId == columnId);
        var visible = Assert.Single(column.VisibleCards);
        Assert.Equal(aliceCard.Id, visible.CardId);
    }

    // Blur-until-reveal visibility

    [Fact]
    public void GetState_UnderBlur_HidesOtherAuthorsCardsAsCounts()
    {
        var board = CreateBoard(blurUntilReveal: true);
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        var columnId = FirstColumnId(board);
        board.AddCard(alice.Id, columnId, "alice 1");
        board.AddCard(bob.Id, columnId, "bob 1");
        board.AddCard(bob.Id, columnId, "bob 2");

        var column = board.GetState(alice.Id).Columns.Single(c => c.ColumnId == columnId);

        var visible = Assert.Single(column.VisibleCards);
        Assert.Equal("alice 1", visible.Text);
        var hidden = Assert.Single(column.HiddenCardCounts);
        Assert.Equal("Bob", hidden.AuthorName);
        Assert.Equal(2, hidden.Count);
    }

    [Fact]
    public void RevealColumn_NonFacilitator_Throws()
    {
        var board = CreateBoard(blurUntilReveal: true);
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        var columnId = FirstColumnId(board);

        Assert.Throws<UnauthorizedAccessException>(() => board.RevealColumn(bob.Id, columnId));
    }

    [Fact]
    public void RevealColumn_MakesEveryonesCardsVisibleToEveryone()
    {
        var board = CreateBoard(blurUntilReveal: true);
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        var columnId = FirstColumnId(board);
        board.AddCard(alice.Id, columnId, "alice 1");
        board.AddCard(bob.Id, columnId, "bob 1");

        board.RevealColumn(alice.Id, columnId);

        var bobsView = board.GetState(bob.Id).Columns.Single(c => c.ColumnId == columnId);
        Assert.Equal(2, bobsView.VisibleCards.Count);
        Assert.Empty(bobsView.HiddenCardCounts);
    }

    // Phase advancement

    [Fact]
    public void AdvancePhase_NonFacilitator_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");

        Assert.Throws<UnauthorizedAccessException>(() => board.AdvancePhase(bob.Id));
    }

    [Fact]
    public void AdvancePhase_WritingToVoting_Succeeds()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");

        board.AdvancePhase(alice.Id);

        Assert.Equal(BoardPhase.Voting, board.Phase);
    }

    [Fact]
    public void AdvancePhase_VotingToActionItems_WithoutEndingVoting_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        board.AdvancePhase(alice.Id);

        Assert.Throws<InvalidOperationException>(() => board.AdvancePhase(alice.Id));
    }

    [Fact]
    public void AdvancePhase_VotingToActionItems_AfterEndingVoting_Succeeds()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        board.AdvancePhase(alice.Id);
        board.EndVoting(alice.Id);

        board.AdvancePhase(alice.Id);

        Assert.Equal(BoardPhase.ActionItems, board.Phase);
    }

    [Fact]
    public void AdvancePhase_PastActionItems_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        board.AdvancePhase(alice.Id);
        board.EndVoting(alice.Id);
        board.AdvancePhase(alice.Id);

        Assert.Throws<InvalidOperationException>(() => board.AdvancePhase(alice.Id));
    }

    [Fact]
    public void EndVoting_NonFacilitator_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        board.AdvancePhase(alice.Id);

        Assert.Throws<UnauthorizedAccessException>(() => board.EndVoting(bob.Id));
    }

    [Fact]
    public void EndVoting_OutsideVotingPhase_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");

        Assert.Throws<InvalidOperationException>(() => board.EndVoting(alice.Id));
    }

    // Voting

    [Fact]
    public void CastVote_OutsideVotingPhase_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var card = board.AddCard(alice.Id, columnId, "text");

        Assert.Throws<InvalidOperationException>(() => board.CastVote(alice.Id, card.Id, 1));
    }

    [Fact]
    public void CastVote_AboveMaxVotesPerCard_Throws()
    {
        var board = CreateBoard(maxVotesPerCard: 3);
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var card = board.AddCard(alice.Id, columnId, "text");
        board.AdvancePhase(alice.Id);

        Assert.Throws<ArgumentOutOfRangeException>(() => board.CastVote(alice.Id, card.Id, 4));
    }

    [Fact]
    public void CastVote_AboveTotalBudget_Throws()
    {
        var board = CreateBoard(voteBudget: 5, maxVotesPerCard: 3);
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var cardA = board.AddCard(alice.Id, columnId, "a");
        var cardB = board.AddCard(alice.Id, columnId, "b");
        board.AdvancePhase(alice.Id);
        board.CastVote(alice.Id, cardA.Id, 3);

        Assert.Throws<InvalidOperationException>(() => board.CastVote(alice.Id, cardB.Id, 3));
    }

    [Fact]
    public void CastVote_ReplacingOwnAllocation_DoesNotDoubleCountAgainstBudget()
    {
        var board = CreateBoard(voteBudget: 5, maxVotesPerCard: 3);
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var card = board.AddCard(alice.Id, columnId, "a");
        board.AdvancePhase(alice.Id);
        board.CastVote(alice.Id, card.Id, 3);

        // Lowering then re-raising the same card's own allocation must not be treated as new spend.
        board.CastVote(alice.Id, card.Id, 2);
        board.CastVote(alice.Id, card.Id, 3);

        Assert.Equal(3, board.GetState(alice.Id).Columns.Single(c => c.ColumnId == columnId)
            .VisibleCards.Single(c => c.CardId == card.Id).MyVoteCount);
    }

    [Fact]
    public void CastVote_HiddenUntilEndVoting_ThenVisibleToEveryone()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        var columnId = FirstColumnId(board);
        var card = board.AddCard(alice.Id, columnId, "a");
        board.AdvancePhase(alice.Id);
        board.CastVote(alice.Id, card.Id, 2);
        board.CastVote(bob.Id, card.Id, 1);

        var bobsViewBeforeReveal = board.GetState(bob.Id).Columns.Single(c => c.ColumnId == columnId)
            .VisibleCards.Single(c => c.CardId == card.Id);
        Assert.Null(bobsViewBeforeReveal.VoteCount);
        Assert.Equal(1, bobsViewBeforeReveal.MyVoteCount);

        board.EndVoting(alice.Id);

        var bobsViewAfterReveal = board.GetState(bob.Id).Columns.Single(c => c.ColumnId == columnId)
            .VisibleCards.Single(c => c.CardId == card.Id);
        Assert.Equal(3, bobsViewAfterReveal.VoteCount);
    }

    // Action items

    [Fact]
    public void ConvertToActionItem_OutsideActionItemsPhase_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var card = board.AddCard(alice.Id, columnId, "text");

        Assert.Throws<InvalidOperationException>(() => board.ConvertToActionItem(alice.Id, card.Id, null, null));
    }

    [Fact]
    public void ConvertToActionItem_NonFacilitator_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");
        var columnId = FirstColumnId(board);
        var card = board.AddCard(alice.Id, columnId, "text");
        board.AdvancePhase(alice.Id);
        board.EndVoting(alice.Id);
        board.AdvancePhase(alice.Id);

        Assert.Throws<UnauthorizedAccessException>(() => board.ConvertToActionItem(bob.Id, card.Id, null, null));
    }

    [Fact]
    public void ConvertToActionItem_ByFacilitator_AddsTrackedActionItem()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var columnId = FirstColumnId(board);
        var card = board.AddCard(alice.Id, columnId, "Automate the deploy");
        board.AdvancePhase(alice.Id);
        board.EndVoting(alice.Id);
        board.AdvancePhase(alice.Id);
        var dueDate = new DateOnly(2026, 9, 1);

        var actionItem = board.ConvertToActionItem(alice.Id, card.Id, "Bob", dueDate);

        Assert.Equal("Automate the deploy", actionItem.Text);
        Assert.Equal("Bob", actionItem.AssigneeName);
        Assert.Equal(dueDate, actionItem.DueDate);
        Assert.Equal(card.Id, actionItem.SourceCardId);
        Assert.Single(board.GetState(alice.Id).ActionItems);
    }

    // Timer

    [Fact]
    public void StartTimer_NonFacilitator_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var bob = board.AddParticipant("Bob");

        Assert.Throws<UnauthorizedAccessException>(() => board.StartTimer(bob.Id, 60));
    }

    [Fact]
    public void StartTimer_NonPositiveDuration_Throws()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");

        Assert.Throws<ArgumentOutOfRangeException>(() => board.StartTimer(alice.Id, 0));
    }

    [Fact]
    public void StartTimer_SetsAFutureEndTimestamp()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        var before = DateTime.UtcNow;

        board.StartTimer(alice.Id, 300);

        Assert.NotNull(board.TimerEndsAtUtc);
        Assert.True(board.TimerEndsAtUtc > before.AddSeconds(299));
    }

    [Fact]
    public void StopTimer_ClearsTheEndTimestamp()
    {
        var board = CreateBoard();
        var alice = board.AddParticipant("Alice");
        board.StartTimer(alice.Id, 300);

        board.StopTimer(alice.Id);

        Assert.Null(board.TimerEndsAtUtc);
    }
}
