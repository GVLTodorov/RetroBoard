using System.Collections.Concurrent;
using RetroBoard.Domain.Templates;

namespace RetroBoard.Domain.Boards;

/// <summary>
/// Pure in-memory board store, no database. A board is fully deleted (not just evicted) when its
/// last participant leaves — see <see cref="Board.RemoveParticipant"/> callers.
/// </summary>
public sealed class InMemoryBoardRepository : IBoardRepository
{
    private readonly ConcurrentDictionary<BoardId, Board> _boards = new();

    public Board? Create(
        string name, TemplateType template, bool blurUntilReveal, int voteBudget, int maxVotesPerCard)
    {
        if (!BoardId.TryParse(name, out var id))
        {
            return null;
        }

        var board = new Board(id, name, template, blurUntilReveal, voteBudget, maxVotesPerCard);
        return _boards.TryAdd(id, board) ? board : null;
    }

    public bool TryGet(BoardId boardId, out Board? board) => _boards.TryGetValue(boardId, out board);

    public void Remove(BoardId boardId) => _boards.TryRemove(boardId, out _);
}

public interface IBoardRepository
{
    /// <returns>
    /// The created board, or <see langword="null"/> if <paramref name="name"/> has no usable
    /// characters to derive a <see cref="BoardId"/> from, or the resulting id is already taken.
    /// </returns>
    Board? Create(string name, TemplateType template, bool blurUntilReveal, int voteBudget, int maxVotesPerCard);

    bool TryGet(BoardId boardId, out Board? board);

    void Remove(BoardId boardId);
}
