---
name: magic-string-constants
description: Whenever a magic string or magic number literal shows up in C# code, move it into a static Constants.cs class instead of leaving it inline. Use whenever writing or reviewing C# code with a hardcoded string or number literal.
---

# Magic String/Number Constants

Whenever you write or come across a magic string or magic number in C# code, move it into a
`Constants.cs` file as a `public const` field on a `public static class`, and reference the constant
instead of the inline literal.

```csharp
namespace RetroBoard.Contracts;

public static class Constants
{
    public const string BoardStateChangedEvent = "BoardStateChanged";
    public const int MaxVotesPerParticipant = 5;
    public const int MaxVotesPerCard = 3;
}
```

File-scoped namespace, `public static class`, `public const` fields — carried over from
[GVLTodorov/PlanningPoker](https://github.com/GVLTodorov/PlanningPoker)'s `Constants.cs`/
`DeckCatalog` pattern. Put `Constants.cs` in the project closest to where the value is used; if
it's shared across projects (e.g. `RetroBoard.Api` and `RetroBoard.Client`), put it in
`RetroBoard.Infrastructure` so both can reference it.
