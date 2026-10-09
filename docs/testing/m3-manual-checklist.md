# M3 manual check — rooms the host owns, on disk and on both screens

Evidence for the M3 exit criterion: *"scenario 1 + scenario 3 fully work; rooms reload after app
restart."* Covers **F5** (room management, host only), **F9** (rooms.json), and the parts of **F6**
(one room at a time, switching) and **F11** (join/leave notices) that room editing touches.

The automated suite covers all of it at service and window level (`JsonRoomStoreTests`,
`RoomPersistenceTests`, `RoomManagementUiTests`, `ChatEngineLoopbackTests`). What this checklist adds is
the two things a test cannot do: **quit the program and start it again**, and **edit the file in
Notepad** — which is literally how F9 is specified.

- **Machine:** __________________  **OS build:** ______________
- **Build (commit):** ______________  **Date:** ____________
- **Tester:** ____________________

## Before starting

1. `dotnet build Lantern.sln -c Debug` succeeds.
2. Note where the file lives: `%LOCALAPPDATA%\Lantern\rooms.json`. Open that folder in Explorer now, so
   it is one click away later.
3. If a `rooms.json` already exists from earlier testing, either keep it (then the default room names in
   the steps below may differ — adapt them) or move it aside to `rooms.json.bak` for a clean run.
4. Since M4, a run uses the Bluetooth radio when the machine has one. This checklist is two windows on one
   machine, so start every instance with `--loopback`
   (`dotnet run --project src/Lantern -- --loopback`) and check the status bar says "over loopback"
   before step 1. Otherwise the joining window will find nothing, for a reason that has nothing to do
   with M3.
4. Two instances side by side, as in the M2 checklist: **A** hosts, **B** joins. Still loopback: no
   Bluetooth, no second machine.

| # | Do this | Expect | ✓ | Notes |
|---|---|---|---|---|
| 1 | Start A as host with a nickname. Then look at `%LOCALAPPDATA%\Lantern\`. | `rooms.json` exists and opens in Notepad as readable JSON listing Room 1, Room 2, Room 3. No `rooms.json.tmp` left behind. | | |
| 2 | Start B as client, connect to A, join Room 1. | B's room list matches A's. Joining shows a notice in the room for both. | | |
| 3 | In A, **New** → type `Garden` → OK. | A new room appears in A's list **and in B's list within about a second**, without B doing anything. | | |
| 4 | In A, **New** → type `garden` (different case only). | The dialog refuses while typing — it does not wait for OK. Rooms are matched case-insensitively, so `garden` and `Garden` would be two rooms nobody can enter unambiguously. | | |
| 5 | In A, **New** → type a name of 25+ characters, then a name of only spaces. | Both refused while typing (24 characters is the limit, and a blank room is not a room). | | |
| 6 | In A, select `Garden` → **Edit ▸ Rename** → `Kitchen` → OK. | Both lists now say `Kitchen`, and neither says `Garden`. Anyone inside `Kitchen` is still inside it, with the transcript intact. | | |
| 7 | In A, select `Kitchen` while B is standing in it → **Delete**. | A confirmation naming the room appears first. **Cancel** and check nothing at all happened — the room is still listed on both sides. | | |
| 8 | Repeat step 7 and confirm this time. | The room goes from both lists. B, who was inside it, sees a notice along the lines of `Kitchen - closed by the host` and ends up **in no room**: transcript cleared, Send disabled, "Select a room to enter". B's connection survives — it joins another room without reconnecting. | | |
| 9 | In A, **Delete** with no room selected. | Nothing is deleted; the status bar says to select a room first. Guessing here would delete whichever room happened to be first in the list. | | |
| 10 | Look at B's window. | B has **no New and no Delete** buttons, and **Edit ▸ Rename** is greyed out. B's room list is read-only: it changes by clicking a room (join/switch) and nothing else. | | |
| 11 | B joins Room 2, then clicks Room 3 in the list. | B leaves Room 2 and joins Room 3 — one room at a time (F6). Room 2's remaining members saw B leave (`B - left this chat`). | | |
| 12 | B clicks **Leave** → refuse, then **Leave** → confirm. | Refusing changes nothing. Confirming puts B nowhere, and Room 3's members get the leave notice. A can rejoin. | | |
| 13 | In A, create a room `Persist Me`, then **File ▸ Quit** (confirm) and press **Start a Server** again on the start screen that came back. | `Persist Me` is in the list again, and so is every other rename/delete made earlier. Rooms that had people in them start **empty** — membership is who is connected now, not who was. | | |
| 14 | In A, click the **✕** on the title bar, then refuse. In B, click the **✕** on the title bar. | A asks first — "everybody is connected to this window" — and refusing means the window is still there with B still connected. The question is not only behind File ▸ Quit: Alt+F4 is the same keystroke. B (a client) closes without asking, because nobody else depends on it staying open. Since v1.0.2 both of them land on the start screen rather than out of the program, and B's row of buttons works again. | | |

| 15 | Immediately after creating another room in A, quit with **Alt+F4** rather than File ▸ Quit. | The room is still there on the next start — hosting again from the same start screen counts as the next start, since the room list is read from the file either way. A change made one second before closing must not be lost. | | |
| 16 | F9, part one: open `rooms.json` in Notepad, add a name by hand, save, restart A. | The hand-added room appears. Anyone may edit this file — it is not a program-private blob. | | |
| 17 | F9, part two: replace the whole file with `["Alpha", "Beta"]`, save, restart A. | Exactly Alpha and Beta. The file is understood in both shapes (object with `rooms`, or a bare array of names). | | |
| 18 | F9, part three: delete every room in A, quit, restart A. | **No rooms**, not the three defaults. Deleting everything is a decision, not damage. Now put junk in the file (`{{{`) and restart: the three defaults come back, and the app starts anyway. | | |
| 19 | Make `rooms.json` read-only (right-click → Properties), then create a room in A while B is connected. | The room appears on both sides and chatting continues. Saving fails, and that is reported (status bar) rather than thrown at the user or silently swallowed. Clear the read-only flag afterwards. | | |
| 20 | Start a second host (call it C) on the same machine while A is still hosting. | Both hosts start; each has its own room list in memory. **Known limitation, not an M3 defect:** on one Windows user account both hosts read and write the *same* `rooms.json`, so whichever quits last wins. One host per machine is the intended deployment; note what you saw. | | |

## Result

- [ ] All 20 steps passed
- [ ] Passed except the notes below (M3 exit acceptable)
- [ ] Failed — raise an issue per step number

Summary / defects raised:

```
Step : defect
-----+---------------------------------------------------------------
     :
     :
```

Deliberate behaviour that is easy to mistake for a defect, so it is written down here:

- **A host window has no Leave button.** Figure 3 gives the host New and Delete only. A host who wants
  out of a room moves to another room or quits. Recorded as an open question in
  `docs/notes/T3.1-T3.7-rooms-on-disk-and-on-screen.md`.
- **The rooms file holds names and nothing else.** No members, no transcripts, no per-room settings —
  see the notes for why.
- **Room list updates are whole lists, not changes.** A client that missed a frame while reconnecting
  would otherwise be permanently wrong about what rooms exist.
