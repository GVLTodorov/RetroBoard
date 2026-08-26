---
name: frontend-conventions
description: CSS/Blazor conventions for RetroBoard.Client — design tokens, component structure, and JS interop. Use whenever adding or editing a Blazor component, styling anything in app.css, or writing a bUnit component test.
---

# Frontend Conventions

Carried over from [GVLTodorov/PlanningPoker](https://github.com/GVLTodorov/PlanningPoker), a
sibling .NET/Blazor project by the same author — these are decisions that project made after real
UI work and real bugs, not ecosystem defaults. Apply them from the start here rather than
rediscovering the same lessons.

## Design tokens — never a literal color or px value

Every color and spacing value should come from a `:root` custom property in
`RetroBoard.Client/wwwroot/css/app.css` — a palette scale, surface/text/danger colors, a spacing
scale, a border radius, a touch-target size (PlanningPoker's own token set is a reasonable
starting shape to copy, then re-themed to RetroBoard's own palette — see
[REQUIREMENTS.MD Section 6](../../../REQUIREMENTS.MD#6-visual-design)). New UI work reuses these
tokens (`var(--color-primary-600)`, `var(--space-3)`, ...) rather than introducing a new literal
value. If a needed shade genuinely doesn't exist yet, add it to `:root` as a new token — don't
inline it once and call it done.

## No scoped `.razor.css` — one shared `app.css`

Follow PlanningPoker's choice: zero component-scoped stylesheets. All styling lives in
`wwwroot/css/app.css`, referenced once from `index.html`. New component styles go into `app.css`
under a comment/section for that component, not into a new `ComponentName.razor.css`.

## Card/overlay contrast

RetroBoard's sticky-note cards and any status indicator drawn over a colored surface (vote count
badge, author initials, phase indicator) need to stay legible against whatever background color
the card/column has — don't rely on a single hardcoded text color. If an indicator sits over a
variable-color surface (e.g. a per-participant color), give it its own solid/semi-opaque backing
rather than bare text — this was a real, reported bug in PlanningPoker's avatar overlays and is
worth designing around up front rather than hitting it again here.

## Name/text labels: never truncate with ellipsis — wrap and reserve height

Participant names and card text are user input of unpredictable length. Prefer `line-clamp` with a
reserved `min-height` (wraps to N lines, doesn't cut off, keeps sibling cards aligned) over a fixed
single line with `text-overflow: ellipsis` — same fix PlanningPoker made for player names, applies
equally to card text here.

## Component structure

- One Blazor component per `.razor` file; `@code` block at the bottom, not a separate `.razor.cs`
  partial.
- Parameters: `[Parameter, EditorRequired]` for anything the component can't function without,
  plain `[Parameter]` otherwise. Parent/child communication via `EventCallback`/`EventCallback<T>`,
  not two-way binding hacks.
- Grid/list layouts for card collections use `repeat(auto-fill, minmax(<min>, 1fr))` so the count
  of visible cards adapts to viewport width without a fixed column count.

## JS interop

- Import the module once per component instance:
  `await JsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/interop.js")`, cache the
  `IJSObjectReference`, then call functions on it.
- **bUnit gotcha**: module-scoped JS calls must be stubbed with
  `JSInterop.SetupModule(path).Setup<T>(...)`, not the bare `JSInterop.Setup<T>(...)` — the latter
  only matches calls made without the `import` indirection and silently fails to match. This cost
  PlanningPoker real debugging time once; don't repeat it here.
