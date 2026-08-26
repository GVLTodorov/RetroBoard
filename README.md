# RetroBoard

<em>Real-time, self-hosted sprint retrospective board.</em>

## What is this?

A single-container web app for running sprint retrospectives: create a board, share a link, add
sticky notes to columns (Went well / Didn't go well / Action items, or another template),
dot-vote on what matters most, and walk away with a tracked action-item list — all over a
real-time connection, no sign-up required.

It's a sibling project to [GVLTodorov/PlanningPoker](https://github.com/GVLTodorov/PlanningPoker)
(same author, same "small self-hosted agile-ceremony tool" shape) and reuses that project's stack
and conventions. See [REQUIREMENTS.MD](REQUIREMENTS.MD) for the full spec.

## Status

Spec-only right now — [REQUIREMENTS.MD](REQUIREMENTS.MD) and this repo's `.claude/skills/`
conventions are in place; implementation hasn't started yet.

## Tech stack

.NET (ASP.NET Core Minimal API) + Blazor WebAssembly, one language end-to-end, single container
image, no separate JS/npm toolchain. Real-time board state pushed over SignalR.

## Planned project structure

Mirrors [PlanningPoker's layout](https://github.com/GVLTodorov/PlanningPoker#project-structure):
a solution file at the repo root, every project under `src/`, split along dependency lines
(`Infrastructure` / `Api` / `Client` / `Tests.*`).
