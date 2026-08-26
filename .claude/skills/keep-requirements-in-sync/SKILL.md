---
name: keep-requirements-in-sync
description: REQUIREMENTS.MD is the living spec for this app, not a historical record of the original ask -- any code change that adds, removes, or changes a user-facing feature, workflow, tooling/CI pipeline, or performance/testing capability must update it in the same change. Use whenever writing or modifying code in RetroBoard, before considering the task done.
---

# Keep REQUIREMENTS.MD In Sync

## The rule

If a change alters what's described in [REQUIREMENTS.MD](../../../REQUIREMENTS.MD) -- a new
feature, a changed workflow, a new CI pipeline, a new testing/perf tool, or a UI detail the doc
already claims (a count, a label, where something appears, who can see it) -- update the doc in
the *same* change that touches the code. Don't treat it as a follow-up or a "someone will get to
it later" item.

Before adding a new bullet, **grep the doc for related existing text first**. Most of the time the
fix isn't a new paragraph, it's correcting a sentence that's already there but now says something
false (a stale count, a stale location, a feature that got removed). Silently going stale is worse
than being verbose.

## Why

This convention is carried over from [GVLTodorov/PlanningPoker](https://github.com/GVLTodorov/PlanningPoker),
where a single alignment pass on that project's requirements doc turned up several real drifts
(wrong counts across doc/README/code, a documented feature that was never built, an entire tool
built to satisfy a requirement with no mention added back to that section). None of that would
have accumulated if each change had updated the doc when it happened. Apply the same discipline
here from the start rather than letting RetroBoard's own doc drift the same way.

## How to apply

**Update the doc for:**
- A new or changed UI element, workflow step, or behavior a user can see/trigger.
- A new project under `src/` (add it to the README's project-structure table too) or a new CI
  workflow -- especially one that fulfills or partially fulfills an existing requirement bullet.
  Reference the workflow file and what it produces.
- Any concrete detail already stated in the doc (a number, a default, a label, a location, who can
  see something) that the change makes inaccurate.
- A capability the doc describes as a candidate/future-work item that gets built.

**Skip the doc for:**
- Pure refactors, renames, or internal implementation changes that don't alter described behavior.
- Bug fixes that restore documented behavior rather than changing it.
- Styling/CSS tweaks that don't change a documented visual detail.
- Test-only changes with no behavior change.

**Where it goes:** match the doc's existing section structure (UX workflows in Section 5, testing
in 8, performance in 9, etc.) rather than bolting a new top-level section on for every change. If
something genuinely doesn't fit an existing section, that's a signal to ask rather than force it
somewhere.

Keep entries factual and terse, matching the doc's existing voice.
