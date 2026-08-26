namespace RetroBoard.Domain.Boards;

public sealed class Participant
{
    public Guid Id { get; }
    public string Name { get; internal set; }
    public bool IsFacilitator { get; internal set; }

    internal Participant(Guid id, string name, bool isFacilitator)
    {
        Id = id;
        Name = name;
        IsFacilitator = isFacilitator;
    }
}
