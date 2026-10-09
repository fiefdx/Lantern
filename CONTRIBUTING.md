# Contributing to Lantern

Working rules for this repository (task **T0.1**).

## Branch rules

* `main` is the only long-lived branch. It must always build clean and run.
* Branch names: `m<n>-t<n>.<n>-<short-slug>`, e.g. `m0-t0.4-startup-form`.
* One task per branch. Keep branches short-lived (a task should be one PR).
* Integrate by **squash merge** through a pull request; `main` stays linear.
* Never force-push to `main`. Never commit `bin/`, `obj/` or `rooms.json`.
* If a task will take more than half a day, push a draft PR early so the
  backup owner can see the direction.

## Definition of Done (group contract)

A task is done only when it:

1. compiles without warnings,
2. runs from the current `main` branch,
3. has been tested by someone who did **not** write it,
4. has a short note explaining how to use it (put it in `docs/notes/<task>.md`
   or in the PR description, then link it here).

## Module owners — one owner + one backup per module

| Module | Area | Owner | Backup |
|:--|:--|:--|:--|
| `UI/` — startup form, shell, dialogs, layout | Application shell | Haitao Yang | Okechukwu Ebogu |
| `Storage/` — `RoomDto`, `JsonRoomStore`, `rooms.json` | Persistence / data | Julia Ferreira Moraes Welerson | Haitao Yang |
| `Domain/`, `Core/` — messages, rooms, users, validation, event wiring | Domain / core logic | Okechukwu Ebogu | Victory Oleru |
| `Transport/` — transport, frames, file transfer, sound, member list | Networking | Victory Oleru | Julia Ferreira Moraes Welerson |
| Tests / release checklist | QA | rotates — never the author of the code under test | — |

## Pull request checklist

* [ ] Build has **0 warnings, 0 errors**
* [ ] `dotnet test` passes
* [ ] Reviewer is not the author
* [ ] "How to use" note added
* [ ] Related task id from `docs/TASK_BREAKDOWN.md` in the PR title

## Code conventions

* **Every form keeps its UI design in the designer partial class.**
  `FooForm.Designer.cs` holds the `components` field, the control fields, and
  `InitializeComponent()` (sizes, positions, anchoring, tab order, event wiring).
  `FooForm.cs` holds only behaviour: constructor, `Load`, event handlers and
  helpers. Never build or position controls in `FooForm.cs` — the Visual Studio
  designer must be able to open the form in design view.
* One class per file, file name = type name, `partial` only for forms/dialogs.
* Nullable reference types are on; no `#pragma warning disable` without a
  one-line reason.
* The UI layer may only use `IChatService` — never `Core`/`Transport` types.

## When the designer will not open a form

If design view shows this instead of the form:

```
The input does not contain any JSON tokens. Expected the input to start with a valid JSON token,
when isFinalBlock is true. LineNumber: 0 | BytePositionInLine: 0.
```

it is not the form's fault and it is not Lantern's. The WinForms designer runs out of process and reads
`src\Lantern\obj\Debug\net10.0-windows\Lantern.designer.deps.json` to learn what the project references. That
file is an *incremental* MSBuild output (`_GenerateDesignerDepsFile`), so once it exists — even at zero bytes,
left behind by a build that was interrupted while writing it, or a disk that filled — nothing rewrites it and
the designer keeps reading nothing. Close the designer tab, delete the file (or all of `obj\`), build once,
and reopen; Visual Studio regenerates it and the form opens. Confirm with the file size: a healthy one is a
few kilobytes, a broken one is 0.

The way to leave one behind is to run the command-line build or tests in the same checkout while Visual Studio
has it open, and kill the run partway through — which is why the command-line verification runs in this project
use a separate output path (`dotnet test Lantern.sln -p:OutputPath=bin/agent/`) so they do not overwrite the
`bin\Debug` assemblies Visual Studio is running. That separates the outputs but **not** the `obj\` intermediates,
so finish a run rather than killing it, and if you do kill one, delete the empty file before blaming the form.

## Commit messages

Use the task id as prefix so history maps back to the breakdown:

```
T0.4 [UI] StartupForm shell with nickname validation
T2.1 [Net] Add Frame/FrameType/FrameCodec length-prefixed protocol
```
