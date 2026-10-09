# A room is what it is, not what it is called

Reported after `v1.0.1`, in one line, as a design complaint rather than a stack trace:

> because the room name can be changed, so you can not use it as identifier

It arrived next to a symptom: click Room 1, then click Room 2, and the window goes to Room 2 while the
list goes back to Room 1. Both turned out to be the same thing, and the design complaint was the right
one to listen to.

## What "the name is the identity" was actually costing

A room's name is editable by the host at any moment (F5). Anything that can be edited cannot be what a
thing is *found* by, and this program was finding rooms by name in five places at once — the window's
list, the client's state, the frames on the wire, the host's engine, and `rooms.json`. Three
consequences, worst last:

**1. Switching rooms lost the highlight.** A room list arriving while a join is in the air was drawn
from a state that did not yet know where the window was going, so it anchored the highlight on the room
the window had been in — the old one — and put it back there. Nothing re-highlighted after the join
succeeded, so the list ended up saying Room 1 while the window was in Room 2. That is the symptom, and
by itself it was four lines of fix: anchor the redraw on *the room this window is on its way to*, and
put the highlight on the room it reached once the join lands.

**2. Frames agreed by spelling.** `JOIN_ROOM` carried a name; `MEMBER_LIST` and every traffic frame
carried a name. A host that renamed a room and a client that had not yet heard were then talking about
two different rooms with one word, and the word was the only evidence either side had.

**3. A rename dropped everybody out of the room.** This is the one the report was pointing at, and it
was worse than a stale highlight.

`Room.Name` is read-only, so a rename has to make a new `Room` object. The host carried the membership
across (`ARenameKeepsWhoeverWasInside`, T3.3 — that much was right) and re-broadcast the list. On this
side, `ClientState` kept its rooms in a dictionary **keyed by name**, and the window held `CurrentRoom`
— so when the renamed list arrived:

* the window's `CurrentRoom` was an object nobody else in the program had any more;
* the transcript on the screen was the transcript of that dead object, so lines said afterwards
  appeared somewhere else;
* the next room list re-keyed everything again, and the room the window thought it stood in was not in
  the list it had just been sent.

Renaming the room you are in moved you out of it. Nothing said so.

## The rule, and where it landed

`Room` now has two names for itself, and the class says which is which: `Id` says *which room*, `Name`
says *what it is called*. Everything that acts on a room uses the first; everything a person reads uses
the second.

| Layer | Before | Now |
|---|---|---|
| `Domain/Room` | name only | `Id` for the room's whole life, through a rename and through a restart; `RenamedTo(newName)` keeps the id, the members **and** the transcript |
| `Storage/rooms.json` | `{"name": "Study"}` | `{"id": "…", "name": "Study"}`; a file with no ids still loads and every room in it gets **its own** fresh id, and a hand-edited file that gives two rooms the same id is corrected rather than merged |
| Wire | `JOIN_ROOM {room: "Study"}`, `ROOM_LIST {rooms: ["Study"]}` | `JOIN_ROOM {roomId}`, `ROOM_LIST {rooms: [{id, name}]}`; `MEMBER_LIST`, `TEXT`, `PICTURE` and `FILE_META` carry the id they route by and the name they display |
| `IChatService` | `JoinRoomAsync(string?)`, `RenameRoomAsync(string, string)`, `DeleteRoomAsync(string)` | `JoinRoomAsync(Guid?)` (null = nowhere), `RenameRoomAsync(Guid, string)`, `DeleteRoomAsync(Guid)`. `CreateRoomAsync(string)` keeps a name: a room nobody has made yet has no identity to be given |
| `ChatServer` | `rooms.FirstOrDefault(r => r.Name == name)` | `FindRoom(Guid?)`; names are consulted in exactly one place — deciding whether a *new* room's name is already taken |
| `ChatForm` | `ListBox` of strings | rows are `RoomRow(Room)`: the row **is** the room, and it says the name. Selection, anchoring and highlighting are by `Id`, and a redraw follows the window's room across its own rename instead of holding the old object |
| File transfer | recipients found by room name | `FileTransferJob.RoomId` / `Transfer.RoomId`, so renaming a room halfway across a 20 MB file does not orphan the people waiting for it |

**A line of chat still carries the name of its room**, and that is not an oversight: `Message.RoomName`
is *history* — what the room was called when the line was written — and not an address. `Room.Adopt` is
the door those lines come in by, past the guard `Append` would otherwise use to reject a line naming a
room the room no longer answers to. Rewriting them would rewrite what somebody read.
`ALinesRoomNameIsHistoryRatherThanAnAddress` is the test that says so.

Two things had to be true for the fake, not just the engine: `FakeChatService` renames with `RenamedTo`
(the same room, identity and transcript carried across), and it announces its rooms on a rename the way
the real service does. A test double that behaves differently about a rename is precisely how a rename
bug survives a green suite.

## What this breaks, said out loud

* **The wire format changed.** A `v1.0.1` client cannot join a `v1.0.2` host: it will send a name where
  an identity is expected and be told the room is not there. Both laptops run the same build. (This is
  in the README's limitations now; the field-test sheet says it before the radios go on.)
* **`rooms.json` changed shape**, and deliberately tolerates the old one, because F9 is checked by
  opening the file in Notepad and because somebody's list from last week has to come back.
* **Names are still the whole interface.** The list says names, the dialogs ask for names, the status bar
  quotes them (`in room "Quiet Corner"`), and a test still says `JoinByNameWhenShownAsync("Room 2")`.
  The one place a name becomes an identity is `TestRooms`, and its lookup is called `IdOf` rather than
  anything that sounds like a room, so it never quietly becomes the way a test refers to a room twice.

`TestRooms.JoinByNameWhenShownAsync` is worth a sentence on its own. Making the client ask by identity
surfaced a race that had been there all along and had never mattered: thirteen engine tests connected a
client and immediately joined it to a room *by name*, which meant asking for a row the client's list
might not have arrived with yet. The name-based join had hidden that; it did not mean the window had
been shown the room. The helper now waits for the room to be listed first, which is what a person can
actually do — you cannot click a row that is not on the screen — and the race is gone instead of hidden.

## Tests

`SwitchingRoomTests` (new, four): the window and the highlight arrive together; switching back finds the
room where the last switch left the highlight; **two clicks and the newer one wins** when the answers
come back in any order (`enteringRoomId` + a token, so an older answer is dropped rather than being
allowed to move the window into a room somebody stopped asking for); and a room **renamed underneath a
window** is still the room it was in — same row, same transcript on screen, and the next line said in
that room lands in the transcript the window is showing.

`ChatEngineLoopbackTests`: `ARenamedRoomKeepsThePeopleWhoWereInIt` (two people in Room 2, the host
renames it, both windows follow, and a message said afterwards still reaches them) and
`ALinesRoomNameIsHistoryRatherThanAnAddress`.

`FrameCodecTests`: `ARoomsIdentitySurvivesTheWire`, `ARoomListCarriesTheIdentityAndTheName`,
`LeavingARoomIsNotJoiningSomeOtherRoom` (null is not room zero).

`JsonRoomStoreTests`: `ARoomKeepsItsIdentityAcrossARestart`, `RoomsThatArriveWithoutIdentitiesGetDifferentOnes`,
`TwoRoomsCannotBorrowOneIdentity`.

Suite: **430 green** (418 before), build has 0 warnings.

## Two older notes were wrong, and are corrected in place

* `T3.1-T3.7` said *"a rename keeps the people inside, because names identify rooms and the membership
  is the room"*. It kept the *membership*, on the host, and lost the *people*: the windows were keyed on
  the name and went on holding the room object the rename had replaced.
* `T2.4-T2.5` described leaving as `JOIN_ROOM` with an empty name. Leaving is now `JOIN_ROOM` with no
  identity at all, which is the same single code path for a different reason.

## The shape of the thing, said once

Anything a person can rename cannot be what things are found by. This program already knew that about
people — a nickname is display, `User.ConnectionId` is the person — and did not know it about rooms,
where the renaming is a menu item and the answer arrives on every other machine in the room. The
general failure is the quieter kind: nothing crashes, a window just goes on showing something that is no
longer what anything else is writing to.
