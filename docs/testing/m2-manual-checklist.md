# M2 manual check — two Lantern windows chatting on one machine

Evidence for the M2 exit criterion: *"use-case scenario 4 (text-only chat) works over loopback with 2+
instances; F3–F7 (text), F11, F12 functionally covered."*

The automated suite (`EndToEndScenarioTests`) proves the same scenarios through real sockets, but it
drives the services, not the windows. This checklist is the part a robot cannot sign off: a person
clicking two windows on one machine. No Bluetooth, no second computer, no network.

- **Machine:** __________________  **OS build:** ______________
- **Build (commit):** ______________  **Date:** ____________
- **Tester:** ____________________

## Before starting

1. .NET 10 SDK installed (`dotnet --version`). No NuGet restore problems: the only packages are
   `InTheHand.Net.Bluetooth` (unused at M2) and `System.Text.Encoding.CodePage`.
2. Nothing else is listening on TCP 43797–43804. If a stray Lantern is still running it will be found by
   the scan and can be joined, which is confusing but not an error; close every Lantern window first.
3. Build once:

   ```
   dotnet build Lantern.sln -c Debug
   ```

4. Windows may show a firewall prompt the first time a Lantern instance listens ("Allow access on
   private networks"). It is talking to 127.0.0.1 only; **Allow** is enough. Record below if this prompt
   was refused and what happened (loopback is not supposed to be filtered, and if it is, M4 will hit it too).

   Firewall prompt seen / handled: ______________________________________

## Two windows

Run two instances side by side. Either:

```
dotnet run --project src/Lantern -- --loopback      # terminal 1
dotnet run --project src/Lantern -- --loopback      # terminal 2
```

The `--loopback` is needed since M4: a Lantern run now uses the Bluetooth radio when it has one, and one
laptop cannot be both ends of a Bluetooth link (T4.1 D1). Without it, B will find nothing to join and the
status bar will say so. Both windows should read "over loopback" at the bottom before you start.

or start one from Visual Studio with Ctrl+F5 (set `--loopback` in Debug ▸ Project Properties ▸ Application
▸ Application arguments) and the second from `bin\Debug\net10.0-windows\Lantern.exe --loopback`.
Move the windows so both are visible. Call them **A** (the one that hosts) and **B** (the one that joins).

| # | Do this | Expect | ✓ | Notes |
|---|---|---|---|---|
| 1 | In A, leave the nickname empty and press **Start a Server**. | A refuses and says why (F1). No half-started server, no second window. | ☐ | |
| 2 | In A, try to type a 17-character nickname. | The box stops at 16 characters — the limit is in the control as well as in the validator, so an over-long nickname cannot even be reached (F1). | ☐ | |
| 3 | In A, type a valid nickname (≤16) and press **Start a Server**. | A's chat window opens. Title says Lantern; status line reports the server is listening; the Rooms pane lists Room 1, Room 2, Room 3 (F10); Members is empty; **Send** is disabled because nobody is in a room yet (F3). | ☐ | |
| 4 | In B, press **Scan**. | A's chat appears in the server list as `<machine name> (127.0.0.1:43797)`. **Join** becomes enabled only when an entry is selected. | ☐ | |
| 5 | In B, pick a nickname and press **Join**. | B's chat window opens. Status reports the connection. Rooms: the same three. Members: empty, Rooms: it is standing in no room yet. | ☐ | |
| 6 | In A, select Room 1. | A joins it: A's Members now lists A's nickname, status shows the room. | ☐ | |
| 7 | In B, select Room 1. | Both windows now list both nicknames (F12), and each shows `B - join this chat` (F11). The notice is greyed/distinguished from ordinary lines (F7). | ☐ | |
| 8 | In A, type a line and press **Send** (or Enter). | The line appears in A immediately, on the right or otherwise marked as yours (F4), and in B shortly after with A's nickname attached. | ☐ | |
| 9 | Reply from B. | Same in both directions. A does **not** receive its own line twice (F4). | ☐ | |
| 10 | Type a 501-character line in B and send. | Refused with the message-length rule; the text stays in the box so it can be edited (F1). | ☐ | |
| 11 | Hold Enter down / click **Send** repeatedly with two lines queued. | Lines arrive complete and in order — no interleaved halves, no duplicated words (T2.7 disables Send while a send is in flight; the client also writes one frame at a time). | ☐ | |
| 12 | In A, select Room 2 in the Rooms list. | Room 1 gets `A - left this chat` and its member list shrinks — moving rooms really does lose a member from the old one — while Room 2 gets `A - join this chat`. A's transcript becomes Room 2's (empty) and its Members is just A. B's window is untouched. Select Room 1 again: the earlier lines are still there (F6). | ☐ | |
| 13 | While A is in Room 2, send a line in Room 1 from B. | A does not see it, and does not see it later either: A was not in the room when it was sent, and the host keeps no history to replay. Write down what the window shows when A returns to Room 1 — the gap should be visible and unexplained-looking rather than silently wrong, which is what M3's persistence is for. | ☐ | |
| 14 | Close B's window (the X button). | A shows `B - left this chat` (F11) and its Members drops to one. No exception dialog anywhere, and A keeps working. | ☐ | |
| 15 | In A, **File → Quit**. | A's chat window closes and A's **start screen comes back** (since v1.0.2; before this the program exited with it). Nothing is left *listening*: B must not be able to join the chat that was just quit. The process staying alive is the point; a chat still running behind the start screen is not. | ☐ | |
| 16 | Start A again, join a room, then stop it while B is connected (**File → Quit**). | B reports the connection was lost **once**, in its own status line, stays open, and keeps its transcript. Its **Send** is disabled. | ☐ | |
| 17 | Start two hosts on one machine at once (A and C both press **Start a Server**). | Both start: the second takes the next free port. **Scan** lists both, by name and port. Joining one does not show the other's rooms. | ☐ | |
| 18 | Join, then rejoin: quit B's chat with **File → Quit**, then from the start screen that came back scan and join the same room again. | Works without restarting A **or** B's program; the room's member list is right; no ghost of the previous connection is listed (F12). | ☐ | |

## Also worth a look while the windows are open

- Resize both windows small: the transcript, rooms and members panes stay usable; the message box stays
  reachable. Nothing overlaps at the smallest size the form allows.
- About (**Help → About**): says this development build talks over a local loopback transport, not
  Bluetooth, so nobody demonstrates it believing it is wireless. It must not claim to be Bluetooth while
  the windows are talking to 127.0.0.1.
- Status lines should always say what happened last (listening, connected to whom, joined which room,
  what failed). A status line stuck on "idle" while messages flow is a defect worth writing down.

## Known at M2 (not defects in this build)

- **Picture and file share are not implemented.** The **Share** button is present per the wireframe; using
  it says file/picture transfer arrives with M5. Do not count that as an M2 failure.
- **No server-side history.** A room remembers what *your* window saw; joining mid-conversation shows what
  arrives from then on. M3 (room persistence) is where that changes; step 13 records the behaviour.
- **Transcript order is arrival order.** Two people typing at the same moment may each see their own line
  first in their own window. This is deliberate: peers have no shared clock. See
  `docs/notes/T2.6-T2.8-discovery-host-window-and-scenarios.md`.
- **Rooms cannot be created, renamed or deleted yet** — M3 (T3.3–T3.6). The Edit menu items are present
  and disabled or refuse politely on the client, which is exactly what they should do.
- **A typed address cannot be entered yet.** `ServerBrowser.TryParseAddress` accepts `host`, `host:port`
  and a bare hostname, and is covered by tests, but no control on the startup window feeds it. That joins
  with Bluetooth device selection in M4.
- Loopback only: this build never touches the radio, so `InTheHand.Net.Bluetooth` is referenced and unused.

## Result

- [ ] All 18 steps passed
- [ ] Passed except the notes below (M2 exit acceptable)
- [ ] Failed — raise an issue per step number

Summary / defects raised:

```
Step : defect
-----+---------------------------------------------------------------
     :
     :
```
