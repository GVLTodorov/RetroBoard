# RetroBoard — Requirements

## 0. Concept

A single-container, self-hosted **sprint retrospective board**: a facilitator creates a board,
shares a link, the team adds sticky notes to columns (e.g. "Went well" / "Didn't go well" /
"Action items"), notes are optionally hidden until a reveal step, the team dot-votes on what
matters most, similar notes get grouped, and the top items become tracked action items — all
live, over a real-time connection, no sign-up required.

Architecture and conventions are patterned directly after
[GVLTodorov/PlanningPoker](https://github.com/GVLTodorov/PlanningPoker), a sibling project by the
same author solving the same class of problem (a small, real-time, self-hosted agile-ceremony
tool). Reuse its decisions below rather than re-deriving them from scratch; deviate only where the
retro domain genuinely differs from planning-poker's (columns/cards/votes instead of
decks/picks/reveal).

## 1. Tech Stack

.NET (ASP.NET Core Minimal API) + Blazor WebAssembly frontend, single container image. One
language (C#) end-to-end, no separate JS/npm toolchain — the API serves the Blazor WASM build as
static files and exposes real-time board state over SignalR.

- The solution (`.sln`/`.slnx`) file lives at the root level of the repository; every project
  (`.csproj`) lives under `src/`, one folder per project, split along dependency lines (mirror
  PlanningPoker's `Infrastructure` / `Api` / `Client` / `Tests.*` split).

## 2. Developer Experience

- Must be debuggable from VS Code with F5 — a working `.vscode/launch.json` (+ `tasks.json` if a
  build step is needed first) is required.
- Any `dotnet build`/`dotnet publish` step added to build tooling (tasks.json, CI) passes
  `/nodeReuse:false`, so repeated F5 sessions don't leave idle MSBuild nodes behind — see
  PlanningPoker's `no-msbuild-node-reuse` convention, carried into this repo's own skill.

## 3. DevOps / CI-CD

- Produce a versioned Docker image, following the same CI pattern used in
  [GVLTodorov/PlanningPoker](https://github.com/GVLTodorov/PlanningPoker) and
  [GVLTodorov/Resume](https://github.com/GVLTodorov/Resume) (tests gate a version bump, which
  gates the image build/push).
- The pipeline must run the full test suite (see [Section 8](#8-testing-strategy)) before
  building/publishing the image.

## 4. Deployment

### 4.1 Target `docker-compose.yml`

- Single container, serves API + Blazor WASM static files.
- No required external integrations/API keys for core functionality (unlike PlanningPoker's
  optional Giphy avatars) — the app must run fully featured with zero configuration. Any future
  optional integration (e.g. Slack/Teams export) follows PlanningPoker's pattern of degrading
  cleanly to "feature not shown" when unconfigured, never erroring.
- Include an example `docker-compose.yml` in the repo.

### 4.2 Operational endpoints

- `GET /healthz` — unauthenticated liveness check (`{"status":"ok"}`), for container
  orchestration / uptime monitoring. Not gated behind the `/api` prefix the board/column
  endpoints use.

## 5. UX Workflows

### 5.1 Workflow A — Home screen (create a board)

1. On load, the **board name** field is auto-populated with a friendly generated value
   (`<random word>-<short id>`), editable. The board's URL is a slug derived from this name, same
   approach as PlanningPoker's room slugs. A taken slug fails board creation with an error asking
   for a different name.
2. Keyboard focus starts on the **your name** field.
3. A **template picker** (dropdown) chooses the board's column layout, fixed at creation time,
   e.g.:
   - `Went well / Didn't go well / Action items`
   - `Start / Stop / Continue`
   - `Mad / Sad / Glad`
   - `4Ls (Liked / Learned / Lacked / Longed for)`
4. The board creator is always the **facilitator** (see [5.3](#53-facilitator--board-ownership)).
5. Once the board is created, its screen header shows a copy-invite button next to the board name
   (icon-only, copy glyph → checkmark, `aria-label`/`title` for accessibility) that copies the
   shareable board link. Visible to every participant, not just the facilitator.
6. **Visual style:** calm, low-distraction palette (candidate: blue/violet); large fonts, buttons,
   and touch targets, matching PlanningPoker's accessibility-minded sizing philosophy (see
   [Section 6](#6-visual-design)).

### 5.2 Workflow B — Joining via a shared link

1. A participant opens the shared board URL.
2. Enter a **name**; keyboard focus starts here. Pressing **Enter** submits the join.
3. **No facilitator toggle is shown** — a participant who joins via link is never the facilitator.

### 5.3 Facilitator / board ownership

- The player who **creates** the board is its **facilitator**, enforced server-side (first
  participant to join), not a client-supplied flag.
- **Only the facilitator can**: start/stop the writing timer, reveal a column's cards (if the
  board uses hidden-until-reveal, see [5.4](#54-card-lifecycle)), delete any card, end voting
  (reveal vote counts), advance the board from writing → voting → action-items phase, and remove a
  participant. Non-facilitator participants see the relevant controls hidden, and any attempt is
  also rejected server-side — never client-hidden only. Merging cards is the one exception: any
  participant can do that (see [5.4.2](#54-card-lifecycle)), not just the facilitator.
- The **template is fixed at creation time** and cannot be changed once inside the board, mirror
  of PlanningPoker's fixed-deck rule.

### 5.4 Card lifecycle

1. **Writing phase**: participants add sticky-note cards to any column. Cards a participant writes
   are visible to everyone immediately by default; a **blur-until-reveal** mode (facilitator
   toggle at board creation) instead shows only a card-count-per-author placeholder to other
   participants until the facilitator reveals that column, to avoid anchoring/groupthink — mirrors
   PlanningPoker's picked/hidden-until-reveal split.
2. **Grouping**: any participant can drag a card onto another to merge them into a stack (a
   lightweight duplicate/similar-idea consolidation, not full text editing of someone else's
   card).
3. **Voting phase** (facilitator advances the board into it): each participant gets a fixed vote
   budget (default 5 dots, configurable at creation) to distribute across cards/stacks, at most N
   per single card (default 3) to prevent one person dumping every vote on one item. Vote counts
   are hidden from other participants until the facilitator ends voting, then all counts reveal at
   once, sorted highest first.
4. **Action items phase**: the facilitator (or any participant — TBD, default to facilitator-only
   like reveal/reset) converts a top-voted card into a tracked **action item** with an optional
   assignee name and due date. Action items persist in a dedicated list separate from the column
   cards.
5. A board isn't deleted the instant it empties — same grace-window rationale as PlanningPoker
   (a refresh briefly drops the only connection before rejoin lands). Grace window default: 15
   seconds.

### 5.5 Timer

- The facilitator can start a synced countdown timer (default 5 minutes, configurable) for the
  writing phase, visible to all participants identically (server-driven, not per-client
  `setTimeout` drift). Reaching zero doesn't force-advance the phase automatically — it's a
  visual/audible nudge, the facilitator still explicitly advances.

### 5.6 Session persistence across a page refresh

The identity a participant picked on the create/join screen (name) is mirrored into
`sessionStorage`, not just kept in memory — same rationale and scoping as PlanningPoker's
[5.6](https://github.com/GVLTodorov/PlanningPoker/blob/main/REQUIREMENTS.MD#56-session-persistence-across-a-page-refresh):
survives an in-tab refresh, doesn't linger across unrelated future visits.

### 5.7 Export

At the end of a retro, the facilitator can export the board (all columns' cards + the action-item
list) as Markdown, for pasting into a wiki/ticket. No PDF/image export in the initial scope —
candidate for future work.

## 6. Visual Design

- Primary palette: calm blue/violet (final shade TBD during implementation — keep it distinct
  from PlanningPoker's green so the two apps don't look like reskins of each other).
- Larger-than-default UI: bigger fonts, buttons, and touch targets; generous spacing —
  accessibility-minded, same philosophy as PlanningPoker's [Section 7](https://github.com/GVLTodorov/PlanningPoker/blob/main/REQUIREMENTS.MD#7-visual-design).

## 7. Documentation & Branding

- `README.md` must be fully populated: what the app is/does, a feature list, a quick-start (local
  dev + Docker) guide, and the tech stack — same shape as PlanningPoker's README.
- A dedicated **SVG icon/logo**, checked into the repo (README header, favicon, Blazor app icon),
  reflecting the calm palette from [Section 6](#6-visual-design).

## 8. Testing Strategy

- Everything must be unit testable:
  - Backend: domain/board logic (column rules, vote-budget enforcement, grouping, action-item
    conversion, board lifecycle).
  - Frontend: Blazor components (template picker, card rendering, vote-count display, timer).
- **Integration tests** covering the real-time flow end-to-end (join → write → reveal → vote →
  advance to action items), and the REST/SignalR API surface.
- The CI pipeline (see [Section 3](#3-devops--ci-cd)) must run both suites and fail the
  build/version-bump/image-push on any failure.
- Collect code coverage (`coverlet.collector`) and render it with ReportGenerator as an HTML
  artifact plus a Markdown job-summary, informational only — same non-gating treatment as
  PlanningPoker.
- `RetroBoard.Tests.Play` drives the real Home/Join/Board UI through Playwright — 1 facilitator +
  4 guests join, write cards, the facilitator reveals blurred columns, everyone votes, the
  facilitator ends voting, advances to action items, and converts a card — and doubles as a live
  smoke test: every wait targets a specific element, so a stuck flow fails with a
  `TimeoutException` instead of silently producing a broken demo. RetroBoard has no Giphy/avatar
  step to drive, unlike PlanningPoker's equivalent tool. Not gated in CI (same non-gating
  treatment as [Section 9](#9-performance)'s load test); triggered manually via the
  [Demo Video workflow](.github/workflows/demo-video-5p.yml), which commits the regenerated
  `docs/demo.gif`.

## 9. Performance

Apply the relevant .NET/Blazor best practices by construction, then measure with real
benchmarks/load tests — same posture as PlanningPoker's
[Section 10](https://github.com/GVLTodorov/PlanningPoker/blob/main/REQUIREMENTS.MD#10-performance).

- Async end-to-end; no sync-over-async on the request/broadcast path.
- `System.Text.Json` source-generated (de)serializers for the SignalR message envelope — the
  hottest path (every card add/vote fans out to every connected participant).
- Broadcast only the state diff a client needs, not the full board payload, where practical.
- Response compression (Brotli/Gzip) for the static Blazor WASM assets.
- Track the published Blazor WASM bundle size and flag/fail on regressions past a set threshold,
  since download size is the primary perf metric users actually feel —
  `src/RetroBoard.Client/scripts/check-bundle-size.ps1` checks the Brotli-compressed `_framework`
  total against a 4 MiB budget (measured baseline ~2.3 MB), same script PlanningPoker uses. Runs
  as CI's `bundle-size` job, non-gating (informational, like coverage).
- Micro-benchmarks (BenchmarkDotNet) for hot paths: domain mutation (`AddCard`/`CastVote`/
  `GetState`) and hub-message serialization (source-generated vs. reflection). CI's `benchmarks`
  job runs `RetroBoard.Tests.Benchmarks` with a shortened job (`--job short`) and publishes each
  class's results table to the job summary plus the full CSV/HTML/markdown artifacts, non-gating
  — same treatment as bundle-size.
- Expected rule violations (non-facilitator trying to reveal/advance/remove, voting past budget,
  ...) raised as built-in .NET exception types, translated to `HubException` by a single SignalR
  hub filter — same tradeoff PlanningPoker made, not a custom exception hierarchy.
- A load test simulating many concurrent boards and participants writing rapidly, measuring
  broadcast latency under load, not just correctness — same
  `Tests.LoadTest`-style tooling PlanningPoker built. `RetroBoard.Tests.LoadTest` spins up N boards
  x M participants over real SignalR connections, has every participant write K cards
  (round-robin across the board's columns), then has each board's facilitator export it (Section
  5.7) — reporting the `AddCard` round trip's p50/p95/p99/max latency (the hottest path: every
  card add fans out an individualized per-viewer snapshot to every connected participant on that
  board) and failing on any export error. Board-arrival is throttled client-side (a bounded number
  of boards connect at once) purely to stay under a single test-driver machine's ephemeral port
  ceiling at very large scale (e.g. 100 x 100 = 10,000 connections) — it doesn't reduce the scale
  being tested. Not gated into CI's `test` job; triggered manually (100 boards x 100 participants
  x 5 cards by default) via the
  [Load Test workflow](.github/workflows/load-test-100.yml).
