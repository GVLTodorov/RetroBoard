---
name: contracts-naming
description: Naming convention for wire model types in RetroBoard.Infrastructure/Contracts — Request/Response suffixes, not "Dto". Use whenever adding, renaming, or reviewing a type under RetroBoard.Contracts (REST bodies, SignalR hub results, or any nested field type inside one).
---

# Contracts Naming

Carried over from [GVLTodorov/PlanningPoker](https://github.com/GVLTodorov/PlanningPoker), a
sibling .NET project by the same author, which settled on `Request`/`Response` over `Dto` for
every wire model.

## The rule

**Every type in `RetroBoard.Contracts` gets `Request` or `Response`, uniformly** — including types
that are only ever a *nested field* inside a larger response, never a response body themselves.

```csharp
public sealed record BoardStateResponse(
    string BoardId, string Name, TemplateType Template, BoardPhase Phase,
    IReadOnlyList<ColumnResponse> Columns);   // ColumnResponse is never returned on its own,
                                               // still gets the suffix

public sealed record ColumnResponse(
    string ColumnId, string Title, IReadOnlyList<CardResponse> Cards);

public sealed record CardResponse(
    string CardId, string Text, string AuthorName, int VoteCount);
```

The "purer" alternative would drop the suffix on nested-only types and leave them bare (`Column`,
`Card`). Don't do that here — see [why](#why-uniform-not-just-real-bodies) below.

## Why uniform, not just real bodies

Once a Domain layer exists with its own `Board`/`Column`/`Card` types, any extension code mapping
between Domain and Contracts will need to keep the two apart — PlanningPoker's
`ContractExtensions.cs` has to alias-import the domain side (`using DomainCardOption = ...`) to
avoid a name clash. Naming the contract side `CardResponse`/`ColumnResponse` — instead of bare
`Card`/`Column` — is what keeps that mapping code from needing an alias on *both* sides. Bare
nested-type names would just move the collision, not remove it.

## What's exempt

**SignalR push/event messages are not part of this convention.** A broadcast like
`CardAdded`/`VotesRevealed` has nothing "requesting" it, so neither `Request` nor `Response` fits
— name it for the event it represents. A hub RPC *return value* (the result of calling e.g.
`JoinBoard`) does get the suffix (`JoinBoardResponse`), since it's a real response to a specific
call, not a broadcast — mirror PlanningPoker's `JoinRoomResponse`/`PlayerPickStatusChanged` split.

**The Domain layer is untouched.** Domain types (`Board`, `Column`, `Card`) keep plain names with
no `Request`/`Response` suffix — those aren't wire models, they're the domain's own vocabulary.
This convention applies only to `RetroBoard.Contracts`.

## Domain → Contract mapping methods: name each conversion, don't overload `ToContract()`

Name each Domain → Contract extension method after what it *returns* (`ToCardResponse()`,
`ToColumnResponse()`), not a generic `ToContract()`/`ToDto()`/`ToModel()` overloaded by parameter
type. PlanningPoker hit this exact problem — four overloads of one `ToContract()` name, readable at
the call site but not at the declaration — and split them into named methods instead; start this
repo with the named form from the beginning rather than repeating that churn later.
