---
name: no-msbuild-node-reuse
description: Keep MSBuild node reuse disabled for this repo's build tooling so idle dotnet.exe/MSBuild.exe processes don't pile up in Task Manager after repeated F5 debug sessions. Use whenever adding or editing a VS Code task, launch config, or CI step that invokes `dotnet build`/`dotnet publish` in RetroBoard.
---

# No MSBuild Node Reuse

## The rule

Any `dotnet build` (or `dotnet publish`) invocation added to this repo's build tooling —
`.vscode/tasks.json`, CI scripts, new dev-loop scripts — must pass `/nodeReuse:false`.

```json
"args": [
  "build",
  "${workspaceFolder}/RetroBoard.slnx",
  "/property:GenerateFullPaths=true",
  "/consoleloggerparameters:NoSummary",
  "/nodeReuse:false"
]
```

## Why

By default MSBuild keeps its worker nodes (`MSBuild.dll` under `dotnet.exe`, shown generically as
".NET Host" in Task Manager) alive after a build finishes, to speed up the *next* build. If
`.vscode/tasks.json`'s `build` task runs as a `preLaunchTask` before every F5 debug session,
repeated F5/Stop cycles over a day let those idle nodes accumulate (~40MB each, 0% CPU) and get
mistaken for a debug process that "won't die" on Stop, when the actual debuggee had already
terminated fine. This is the exact issue
[GVLTodorov/PlanningPoker](https://github.com/GVLTodorov/PlanningPoker) hit and fixed; apply the
same flag here from the start rather than rediscovering it.

`/nodeReuse:false` makes each build's MSBuild process exit when that build finishes, trading a
small amount of rebuild speed for not leaving anything behind.

## How to apply

- New build-related VS Code tasks or scripts: include `/nodeReuse:false` in the `dotnet build`/
  `dotnet publish` args.
- If idle nodes have already piled up (e.g. from running `dotnet build` manually without the
  flag), clear them with:
  ```powershell
  dotnet build-server shutdown
  ```
- Don't reach for `/nodeReuse:false` as a fix for an actual stuck *debuggee* process — verify with
  `Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'"` (or similar) which processes are
  MSBuild nodes (`MSBuild.dll /nodemode:1`) vs. the app itself before assuming this is the cause.
