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
  helpers. Never build or position controls in `FooForm.cs`. This is so that
  design view can open the form — see the next two bullets, which are what makes
  that true rather than merely wished for.
* **Every window has a constructor with no arguments, and it calls
  `InitializeComponent()` itself.** The Windows Forms designer builds a class in
  order to draw it, so a window with only argument-taking constructors is a
  window nobody can move a control in. Do not "tidy" the constructor into
  `Foo() : this(theService)`: that leaves the design surface's own constructor
  without the call it looks for, and assembles the window being drawn by a route
  the window that ships never takes. Both constructors call
  `InitializeComponent()`, then meet in one method (`SetUp`, `Present`, ...) so
  that only the call is duplicated and no behaviour is.
* **A window still cannot be built without what it shows.** Each parameterless
  constructor asks `UI/DesignTime.cs` whether it is being drawn or being used,
  and gets a stand-in chat / loopback / sample wording while drawn and an
  `InvalidOperationException` anywhere else. `DesignTime` is the only place that
  question is asked; do not answer it a second time inside a window, and do not
  widen a constructor to dodge it — `DesignViewTests` keeps both halves.
* **If a control answers what somebody types, the wiring is part of the design and the test types into it.**
  A greyed button and a line of red are claims about the text in the box *now*, so they are only true as long as
  something re-runs the rules on every keystroke — and `TextChanged` sitting unwired in `InitializeComponent()`
  is invisible to a test that sets a property that happens to validate on the way in. Type one character at a
  time through `SelectedText`; see `NewRoomDialogTests.TypeInto` and
  `docs/notes/typing-in-the-room-name-dialog.md`, which is where that rule was paid for.
* **No `DialogResult` on a button whose click handler can refuse.** A button that carries one has the framework
  hand the form that answer once the handler has run, happy or not — so the dialog's own guard is bypassed by the
  thing that closed it. The form's result belongs to the code that checked (`Accept()`), not to the control.
  `DialogResult.Cancel` on a Cancel button is fine and normal: nobody has to vet "I take it back".
* **A feature the suite cannot perceive gets measured, or gets looked at.** Nothing here has ears or eyes: a cue
  reaches the tests as a value of an enum and a colour reaches them as a symbol. So anything a person is supposed
  to *hear* is measured in the test (`SoundNotifierTests` reads the wav's format, length and peak level), anything
  written in the transcript is measured as a contrast ratio against the surface it sits on
  (`TranscriptLayoutTests`), and the surface itself is compared with the other surfaces in the same window — four
  of them are one surface. Both rules were paid for: see `docs/notes/hearing-it-and-seeing-it.md`.
* **When in doubt, draw the window and open the picture.** A test can build a form, `Show()` it, `DrawToBitmap`
  into a `Bitmap`, save it as a PNG in `Path.GetTempPath()` and write the path with `ITestOutputHelper` — then
  *look at it*, and sample a pixel or two. It is the only way to know what a person sees, and it is the way the
  grey chat area was found: about a minute of work, and it settles an argument that screenshots in a report
  cannot. (Close the form at the end of such a test or hand the teardown over: `HostForm` asks before a host
  quits, and `Sta.Run` now throws rather than reporting a stuck test as passing.)
* **A test that reads what a finished operation *said* has to pump the window's queue first.** A save finishes on
  a worker thread and posts its status line back with `OnUi`; a test that only `await`s has no message loop to
  dispatch it, so the line sits in the queue unread and the assertion fails against a program that said the right
  thing. Pump (`Application.DoEvents()` in a short loop with a `Task.Delay`) before reading the bar; a file on
  disk or a fake's log needs no pump, which is why this is invisible half the time and baffling the other half.
  See `SharedThingsTests.TheWindowCatchingUpAsync`.
* **Size a test's fixture by the property, not by hope.** `APhotoBiggerThanTheWindowIsShownWholeAndScrollable`
  used a 4000 × 3000 photo (48 MB of GDI+) on the theory that no window is that wide: it passed alone and was
  refused partway through a run of five hundred windows. "Bigger than the window" is a property of the window, so
  the test makes the window 600 pixels wide and the photo 1400 — same claim, one thirteenth the memory, and no
  dependence on how big somebody's monitor is. A test that fails only in company gets deleted as flaky.
* **The last metre of an affordance gets one test.** A rule answered by a method is not the same thing as a menu
  being attached, a handler being wired, or a shortcut reaching the control. Something has to raise the real thing:
  `PerformClick()` on a shown form for a button, `ProcessCmdKey`/`SendKeys` for a shortcut, and for a context menu
  a `WM_CONTEXTMENU` posted to the control with the popup's `Visible` and its item text asserted
  (`SharedThingsTests.RightClickOn`). Three shipped defects have sat in this gap: a dialog's OK button no test had
  ever clicked (v1.1.1), a save menu no test had ever opened (v1.2), and a picture viewer that had never displayed
  a picture (v1.2.2).
* **A test that overrides the thing under test and records the call is not evidence that it happened.**
  `protected override void OpenPicture(PictureMessage p) => Opened.Add(p);` is the right seam for asking *which*
  picture a click meant, and it also opens no window — so eighteen green tests about opening pictures had never
  produced one to look at. That is fine as long as something else does the real thing once, in a real window, with
  the pixels checked: `PictureOnScreenTests` samples the middle of the drawn viewer, and when the picture is not
  handed to the control that paints it, it fails with `it was #2D2D30` — the colour of the empty panel.
* One class per file, file name = type name, `partial` only for forms/dialogs.
* Nullable reference types are on; no `#pragma warning disable` without a
  one-line reason.
* The UI layer may only use `IChatService` — never `Core`/`Transport` types.

## Designing a form in Visual Studio

Design view works for all six windows (`v1.0.4`, `docs/notes/design-view.md`). Three things about it are worth
knowing before the first drag:

* **The shared chat layout is designed by opening `ChatForm` [Design].** `HostForm` and `ClientForm` surfaces
  show only the controls those windows add — the base form's control fields are `private` to the base, which is
  the ordinary price of form inheritance.
* **Commit before you drag.** The design serializer owns the body of
  `InitializeComponent()`, and 182 comment lines — the reasons a control is anchored as it is, why a tooltip
  says what it says — live in there. It does not promise them. Read `git diff` afterwards: that is also how to
  catch a rename that the `Click += Handler` wiring in the same body depended on.
* **The door the designer uses is not a door the program can use.** A window
  built without its chat throws, on purpose, with a message saying so. If a test or a piece of product code
  needs a window, it is given the chat like everything else.

## When the designer will not open a form

If design view shows this instead of the form:

```
The input does not contain any JSON tokens. Expected the input to start with a valid JSON token,
when isFinalBlock is true. LineNumber: 0 | BytePositionInLine: 0.
```

it is not the form's fault. The message says one thing precisely: **a dependency file was parsed empty**. The
WinForms designer runs out of process and reads
`src\Lantern\obj\Debug\net10.0-windows\Lantern.designer.deps.json` to learn what the project references. That
file is an *incremental* MSBuild output (`_GenerateDesignerDepsFile`), so once it exists — even at zero bytes,
left behind by a build interrupted while writing it, or on a disk that filled — nothing rewrites it, and the
designer keeps reading nothing. Close the designer tab, delete the file (or all of `obj\`), build once, reopen.
Confirm with the file size: healthy is a couple of kilobytes, broken is 0.

How a zero-byte one gets made: running the command-line build or tests in a checkout while Visual Studio has
it open, and killing the run partway through — which is why the command-line verification runs here use a
separate output path (`dotnet test Lantern.sln -p:OutputPath=bin/agent/`) so they do not overwrite the
`bin\Debug` assemblies Visual Studio is running. That separates the outputs but **not** the `obj\`
intermediates, so finish a run rather than killing it; and if you do kill one, delete the empty file before
blaming the form.

### If deleting it does not help, stop looking in the checkout

The reported stack ends in `NetCoreServerLayout.InjectFormattersDependencyAndCache`, which is the design host
preparing **its own** dependency files — it has not loaded anything of ours yet, so no change to Lantern's
code, csproj or designer files can move it. Two ways to be certain rather than to argue:

* Point the designer's own dependency reader at our files and read what it says. Everything Lantern produces
  parses; only an empty file throws this message. The class is `DependencyContextJsonReader`, in
  `Microsoft.Extensions.DependencyModel.dll` — which ships in
  `…\CommonExtensions\Microsoft\Windows.Forms\DesignToolsServer\Common\`, next to the frame named in the trace.
* Look at what the host copied for itself. It shadow-copies its server into
  `%LOCALAPPDATA%\Microsoft\VisualStudio\18.0_*\WinFormsDesigner\<something>\` and must generate that copy's
  `.deps.json` and `.runtimeconfig.json` before it can launch it. A half-built cache holds the server's
  assemblies and **no dependency files at all** — and every later attempt reuses it and fails identically,
  which is why the error survives deleting, rebuilding and restarting.

Clear the caches and let the host rebuild them:

```powershell
Remove-Item "$env:LOCALAPPDATA\Microsoft\VisualStudio\18.0_*\WinFormsDesigner" -Recurse -Force
Remove-Item "$env:LOCALAPPDATA\Microsoft\VisualStudio\18.0_*\Designer" -Recurse -Force
```

If they come back half-built, the extension's registration is broken and only the installer can put it right:
**Modify → Repair, run as Administrator**. On the working machine the setup run of 8 October 2026 failed with
`0x80070005` creating `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Update\TargetingInfo\PersistentDynamicInstalled`
(`dd_setup_20261008205309_errors.log`). The key it could not write is under HKLM, so a repair that is not
elevated fails the same way; and a repair wants more free space than a nearly-full system drive has to give.

Clearing the caches did in the end fix it: the rebuilt shadow cache has its `DesignToolsServer.deps.json` and
`.runtimeconfig.json`, which the half-built one did not. So the order in this section is the order that worked.

Something said while it was still being diagnosed, corrected here so the note stays trustworthy: the broken cache
also held a zero-byte file called `%WinFormsShadowCacheFlagFile%`, which looked like an unexpanded feature-flag
token and so looked like the cause. The **healthy** cache contains the same file, unchanged. It is Visual Studio
being untidy — not the fault. The lesson belongs with the rules above: a symptom is evidence only once something
else has ruled it out, and a cache that is *missing a file* is evidence in a way that a cache with an odd file in
it is not.

## Commit messages

Use the task id as prefix so history maps back to the breakdown:

```
T0.4 [UI] StartupForm shell with nickname validation
T2.1 [Net] Add Frame/FrameType/FrameCodec length-prefixed protocol
```
