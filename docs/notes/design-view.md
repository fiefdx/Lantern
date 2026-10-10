# Every window opens in the design view

One report after `v1.0.3`:

> I need the design view working for every Form, Dialog, so we can modify ui by the gui designer

It was not working, and not by accident. The Windows Forms design host **constructs the class in order to draw
it**, and every window here is constructed with the thing it shows — `HostForm(IChatService)`,
`ConfirmDialog(string, string)` (private), `StartupForm(ITransportFactory, IKnownHosts)`. So five of the six
windows had no constructor the design surface could call, and design view could only ever have said *"can be
edited, but not designed"*. `CONTRIBUTING.md` claimed the opposite ("the Visual Studio designer must be able to
open the form in design view"); that sentence has never been true of the chat windows or the dialogs, and it
has now been replaced with what is actually the rule.

## The door, and who may use it

`UI/DesignTime.cs` is new, and it is the only place where **being drawn** and **being used** are told apart:

```csharp
internal static bool IsHosting()
    => LicenseManager.UsageMode == LicenseUsageMode.Designtime
       || programIsDesignToolsServer();   // Environment.ProcessPath
```

Two probes because they fail differently. The framework one knows nothing about this project; the other knows
nothing about design at all, which is why it can be trusted — the design host is a separate program
(`DesignToolsServer.exe`, next to Visual Studio's Windows Forms components), so the name of the running
executable says who asked for the window.

Then each window asks it, in the argument list, so nothing is built if the answer is no:

| Window | What the design surface gets | Anywhere else |
|---|---|---|
| `ChatForm`, `HostForm` | a `FakeChatService("Lantern", isHost: true)` | `InvalidOperationException` |
| `ClientForm` | the same with `isHost: false`, so the bar reads *connected* | `InvalidOperationException` |
| `StartupForm` | `Transports.ForStartup(Loopback)` and **no** known-hosts file | unchanged — this is the production entry point |
| `ConfirmDialog` | `"Confirm"` / `"Are you sure?"` | `InvalidOperationException`, naming `ConfirmQuit` and friends |
| `NewRoomDialog` | `"New Room"`, empty | **works** — an empty room to name is a sensible thing to build |

**The designer's door is not a program's door.** Every constructor that used to be the only way in still is the
only way in from the program's side; the invariant "a window that exists has a chat to talk to" is now kept by
`DesignTime` instead of by the absence of an overload, and `DesignViewTests` says so twice — once by reflecting
that every window has the constructor the designer needs, once by constructing all four refusals.

`ChatForm` also stopped being `abstract`. An abstract class is a file design view can never open, and the
shared layout — transcript, room list, member list, status bar, most of both windows — lives in it. It is
concrete now, and the keeping is done by the shape instead: exactly one **public** constructor, the parameterless
one, while the constructor that takes a chat stays `protected` (`TheChatWindowIsOpenToTheDesignerAndToNobodyElse`
asserts that shape).

## The thing not to "tidy up" later

Every parameterless constructor calls `InitializeComponent()` **itself**, and both constructors of a window then
meet in one method (`SetUp`, `AddRoomButtons`, `Present`). Chaining `Foo() : this(theService)` is one line
shorter, and it puts the design surface's statements in a constructor the design surface is not using — so the
window being drawn is assembled by a route the window that ships never takes. The duplication is one call per
class on purpose; the behaviour behind it is shared.

## Editing a designer file, which is now possible and therefore a risk

182 comment lines live *inside* `InitializeComponent()` — the reasons a control is anchored the way it is, why a
tooltip says what it says. The design serializer owns that method's body and does not promise them. Commit
before dragging a control, and read `git diff` afterwards; that is also how to notice a rename that the
`Click += Handler` wiring in the same method quietly depended on.

Shared layout is designed by opening **`ChatForm` [Design]**. `HostForm`/`ClientForm` surfaces show only what
those windows add, because the base form's control fields are `private` to the base — that is the ordinary cost
of form inheritance, and it is where the 64 ms of a loopback chat differs from the ordinary cost of doing
layout in a file by hand.

## Checked

A throwaway console app referencing the built `Lantern.dll`, run twice: once under its own name and once under
a copy of its own app-host renamed to `DesignToolsServer.exe`. Nothing about "am I the designer" is unit-testable
from the test assembly (the seam is `internal`, and no `InternalsVisibleTo`), so it was measured instead:

```
process: DtProbe.exe               →  StartupForm   built
                                      NewRoomDialog built
                                      ChatForm      refused — "belongs to a chat…"
                                      HostForm      refused
                                      ClientForm    refused
                                      ConfirmDialog refused — "…Ask ConfirmQuit…"

process: DesignToolsServer.exe     →  StartupForm   built   lblStatus "over loopback, on this machine alone"
                                                               lstServers 0 items
                                      ChatForm      built   lblStatus "server online"   rooms 3   members 3
                                      HostForm      built   Text "Lantern - host"       rooms 3   members 3
                                      ClientForm    built   Text "Lantern - client"     rooms 3   members 3
                                      NewRoomDialog built   Text "New Room"
                                      ConfirmDialog built   Text "Confirm"
```

Two things in that output were not assumed and are worth naming: the start window drawn by the design host
never enumerated a radio, and it never opened the user's chat history. A window that reaches for the Bluetooth
stack in its constructor is a window that makes Visual Studio wait for a driver, which is exactly the kind of
thing that would have been blamed on the designer rather than on the window.

`DesignViewTests`, six: every window has the parameterless constructor · none of them is abstract · the shared
chat window has exactly one public constructor and it takes nothing · `ChatForm`/`HostForm`/`ClientForm` refuse
to be built without a chat · `ConfirmDialog` refuses to be built without its question · a blank room dialog is a
whole room dialog.

Suite: **454 green** (438 before), build has 0 warnings.

One caveat, and it is the kind that gets forgotten: this was measured by *simulating the designer's identity*,
not by driving Visual Studio. The probe proves the guard, the stand-in chat, the loopback transport and the six
constructors. Only a person opening `HostForm [Design]` proves that this version of the design host is content
with chained-free constructors, private base fields and `#nullable disable` at the top of the designer file. If
a form still refuses, the message it gives is the answer, and it is worth reporting rather than working around. (And on the machine this was built on, the designer host itself was
broken by a setup run — which is a different fault from the six constructors, and is written up in
`CONTRIBUTING.md` under *If deleting it does not help, stop looking in the checkout*.)

## The shape of the thing, said once

An invariant enforced by the *absence* of a door is indistinguishable from a limitation, and the first person
who needs the door for a legitimate reason will remove the invariant with it. Enforce it where the two cases
actually differ — one question, asked once, answered by something outside the program's control — and the door
can stay as wide as the legitimate reason needs.
