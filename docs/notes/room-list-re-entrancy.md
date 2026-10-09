# A room list is not somebody clicking the room list

Reported after `v1.0`, in the debugger, as:

```
System.StackOverflowException  (HResult 0x800703E9)
ChatForm.cs:201:     lstRooms.SelectedIndex = index;
```

`DevHub.exe` in the title bar was a red herring in the previous session; this one is ours, and it is a
design fault rather than a typo: three individually reasonable facts about the room list forming a
circle.

## The circle

1. `ShowRooms()` rebuilds the list from `Service.Rooms`. Emptying it drops the selection, so putting
   the selection back is **a change to the list like any other** — it raises `SelectedIndexChanged`.
2. `LstRooms_SelectedIndexChanged` means "the person chose this room", so it calls `EnterRoomAsync`.
3. `EnterRoomAsync` asks the service to join. A join is exactly how a room list gets sent back
   (`ChatServer.SendRooms`; the client raises `RoomsChanged` on every `ROOM_LIST`).

The guard against that was there — `syncing` — but it was let go of **before** the selection was
restored:

```csharp
lstRooms.EndUpdate();
syncing = false;              // <-- back open here
var index = lstRooms.Items.IndexOf((object?)selected ?? string.Empty);
if (index >= 0)
{
    lstRooms.SelectedIndex = index;   // <-- and the click simulated here, line 201
}
```

so every redraw simulated a click. The only thing that kept most redraws harmless was the name check
in the handler (`name != CurrentRoom?.Name`). That check fails open in one state: **the list is
highlighting a room this window is not in** — which is precisely the state a refused or failed join
leaves behind, since nothing took the highlight off the room that said no.

From there: room list → click → join → room list → click → join …

## Why it overflowed rather than spun

`OnUi` runs the action **on the calling thread** when no marshalling is needed. For a client over
Bluetooth that is the read-loop thread, so each turn is queued and the window busy-spins. For **the
host's own window** — and for `--fake`, and for every test — the chat engine is in process and the
room list is raised *inside* `JoinRoomAsync`, while the join is still on the stack:

```
ShowRooms                    (restores the selection, line 201)
  LstRooms_SelectedIndexChanged
    EnterRoomAsync -> RunAsync -> JoinRoomAsync
      RoomsChanged  (in process, same thread)
        OnUi -> ShowRooms      (deeper still)
```

Each turn stacked about eight frames on the last and nothing was ever assigned to `CurrentRoom`,
because no turn ever got past the `await`. The process died on the line in the report.

Two things were wrong, and both are fixed:

* **a redraw is not a request to join.** The guard now covers the whole method, with a `finally` so
  an exception cannot leave the list permanently deaf, and a nested refresh is remembered
  (`roomsRefreshPending`) instead of nesting — bounded by `MaxRoomRepaints` so a service that
  floods us is stopped rather than being allowed to grow the stack.
* **the list is anchored on where the window IS, not on whatever was highlighted.** After a join that
  did not land, `HighlightWhereIAm()` puts the highlight back on the room the window is really in, or
  off everything when it is in none — and the bar says why.

The second one is a bug on its own, and easy to walk into by hand: stand in Room 1, click a Room 2
that will not have you, then have the host delete Room 2. Before the fix the window was dragged out
of Room 1, because the redraw was busy preserving a highlight for a room that had just vanished, and
a missing highlight looked like a missing room.

`ChatForm.RepaintRooms` now leaves a room only when **the room this window is in** has gone. A room
it was never in going away is somebody else's news.

## What was added to the fake

`FakeChatService` gained three things, each describing something the real service genuinely does —
they are not inventions to make a test pass:

| Seam | The real behaviour it stands in for |
|---|---|
| `JoinRequests` | every join asked for, refused ones included: the count is how a test tells a person clicking twice from a window asking itself |
| `AnnounceRoomsOnJoin` | the host's own window gets `RoomsChanged` **inside** the join call, on its own thread, because its engine is in process |
| `RefuseRoomNamed` | `ChatServer.RefuseJoin` (T6.1) sends a room list out with the refusal |

## The tests

`src/Lantern.Tests/RoomListRefreshTests.cs`, five of them. Three fail on the old code, and they fail
on the thing that matters:

```
ARoomThatWillNotHaveUsIsNotKnockedOnAgain
    Assert.Equal() Failure: Values differ    Expected: 1   Actual: 41
TheHighlightComesOffARoomTheWindowDidNotGetInto
    Assert.Null() Failure: Value is not null Expected: null  Actual: "Room 2"
ARoomVanishingSomewhereElseDoesNotPullTheWindowOutOfItsRoom
    Assert.Equal() Failure: Strings differ   Expected: "Room 1"  Actual: null
```

Forty-one asks for one room from one click; a highlight naming a room the window never entered; and a
window thrown out of the room it was standing in because a room it was never in got deleted.

Proving that needed care: the honest way to demonstrate the old behaviour is to run it, and a real
stack overflow takes the whole test host down with it, which would have looked like a broken suite
rather than a fixed bug. So the old `ShowRooms` was restored **with a depth probe** that throws after
forty nested redraws, the four new tests were run against it, and the file was then put back. The
probe, its script and the temporary revert are not in the commit.

The two that pass either way are kept deliberately: `ARoomListFromTheServiceIsNotSomebodyClickingTheRoomList`
and `AWindowStillWorksAfterARoomHasRefusedIt` are there because of how this breaks next time. The
first is the rule in one assertion. The second exists because the obvious way to fix this bug is a
guard that is never released — and a room list with a stuck guard is a painting: the next room a
person clicks does nothing, forever, with no message anywhere.

Suite: **418 green** (413 before), build has 0 warnings.

## The shape of the thing, said once

A control that reports "the user chose this" also reports "I just redrew", and if choosing starts
work, redrawing starts work too. Every place this program repaints something from service state —
the room list, the member list, the offer strip — has to be able to say which of the two it is, and
the guard belongs around the whole redraw rather than around the part that looks dangerous.
