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

## Commit messages

Use the task id as prefix so history maps back to the breakdown:

```
T0.4 [UI] StartupForm shell with nickname validation
T2.1 [Net] Add Frame/FrameType/FrameCodec length-prefixed protocol
```
