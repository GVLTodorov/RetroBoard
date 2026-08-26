---
name: concise-naming
description: Prefer short, mostly-two-word names for classes/interfaces over longer compound names that just chain qualifiers together. Use whenever naming or renaming a class, interface, or service in this repo.
---

# Concise Naming

Carried over from [GVLTodorov/PlanningPoker](https://github.com/GVLTodorov/PlanningPoker), a
sibling .NET project by the same author.

## The rule

Class and interface names should read as **the most natural, logical name for the concept** —
usually two words, occasionally three when a third word is truly load-bearing. Don't default to
chaining every qualifier that technically applies just because each one is individually accurate.

**Illustrative example**: a service that tracks which SignalR connection belongs to which board
participant is more naturally `ParticipantTracker` than `ParticipantConnectionTracker` —
`Connection` is technically accurate (it's keyed by connection id), but callers care about
*participants* and "tracker" already implies the connection/session angle without spelling it out.
(PlanningPoker made this exact call for its own `PlayerTracker`, originally
`PlayerConnectionTracker` — same reasoning applies here.)

## How to apply

- When naming something new, write the two-word version first (`{Subject}{Role}` — e.g.
  `BoardRepository`, `CardService`, `VoteTracker`) and only add a third word if dropping it would
  make the name ambiguous or collide with an existing type.
- When reviewing an existing name that chains multiple qualifiers (`XyzAbcThing`), ask which word
  is load-bearing and which is redundant with what the class already implies through its
  members/usage — drop the redundant one.
- This is a judgment call, not a mechanical word-count rule — pick whichever reads most naturally
  to someone calling it, not whichever is shortest in isolation.
- Don't rename existing types just to satisfy this convention on sight — apply it when a class is
  already being touched (added, renamed for another reason, or reviewed) rather than as a drive-by
  churn pass.
