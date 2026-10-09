# M4 field test — two or three physical laptops, over the radio

Evidence for the M4 exit criterion: *"scenarios 1, 2, 4 and 6 run over real Bluetooth between laptops;
F2, F3 covered."*

Nothing in the automated suite can stand in for this file. One machine cannot be both ends of a Bluetooth
link, there is no API for choosing between two radios, and a scan on Windows finds almost nothing that is
not already paired — all of which was measured in `docs/notes/T4.1-bluetooth-spike.md`. Everything that
could be decided without a radio is decided and tested there; what is left is whether it works in a room.

**Bring**

- Two laptops (three for the multi-client row), each with a charger, both awake and unsleeped
- The Bluetooth address of each machine, written down. Lantern prints it in the status bar of its own
  startup window: `over Bluetooth, as F8E4E3F9603B`. If a window says `over loopback` instead, that
  machine has no usable radio (or was started with `--loopback`) and cannot take part
- Roughly 40 minutes, and a second pair of hands for the first run
- `docs/testing/bluetooth-pairing-how-to.md` open, for when pairing misbehaves

**Rule for recording:** write down what happened, not what should have. If a step took three tries, that
is the finding, and "it worked in the end" hides it.

- **Build (commit):** ______________  **Date:** ____________
- **Machines:** A ______________  B ______________  C ______________
- **OS build:** __________________  **Adapter(s):** ____________________
- **Runners:** ____________________

---

## 0. Before anything else

| # | Do this | Expect | ✓ | What actually happened |
|---|---|---|---|---|
| 0.1 | On each laptop: Settings → Bluetooth & devices → Bluetooth **on**, and nothing asleep. | The adapter is on and named. | ☐ | |
| 0.2 | Pair A with B (both laptops see the same pairing code and accept it). | Paired, no error. If it fails, stop: everything below depends on it. Follow the how-to's failure table. | ☐ | |
| 0.3 | Start Lantern on each machine **without** `--loopback`. | Each status bar reads `over Bluetooth, as <12 hex digits>`. | ☐ | |
| 0.4 | Write each machine's printed address on paper. | You will need it if step 2 finds nothing. | ☐ | |
| 0.5 | Windows Defender Firewall: allow Lantern when prompted, once per machine. | No prompt appears again during this run. | ☐ | |
| 0.6 | `git describe --tags` on both machines. | The **same build on both**. Since v1.0.2 a frame names a room by its identity rather than by its name, so an older laptop is refused every room it asks for (`docs/notes/room-identity.md`). | ☐ | |

## 1. Host a chat (scenario 1, over the radio)

| # | Do this | Expect | ✓ | What actually happened |
|---|---|---|---|---|
| 1.1 | On A: nickname `Host`, then **Start a Server**. | A's chat window opens; status bar says the server is online. | ☐ | |
| 1.2 | Look at A's room list. | The rooms from A's `rooms.json` are there (default: Room 1, 2, 3). | ☐ | |
| 1.3 | On A: **New** → `Garden`. | `Garden` appears in A's list. | ☐ | |
| 1.4 | Note how long 1.1 took, from click to window. | Under about two seconds. A long pause here is the SDP registration and is worth writing down. | ☐ | |

## 2. Join a chat (scenario 2, over the radio — F3)

| # | Do this | Expect | ✓ | What actually happened |
|---|---|---|---|---|
| 2.1 | On B: nickname `Friend`, then **Scan**. | Status bar reads `scanning Bluetooth…`; the Scan button reads "Scanning…" and clicking it again would stop the scan. | ☐ | |
| 2.2 | Wait for the list. | A appears, by its **device name**, within about twelve seconds. | ☐ | |
| 2.3 | If A does **not** appear: scan again once, then check that A is paired and that A's Lantern is online. | If a second scan still finds nothing, write that down here — this is the known weak point of the design (T4.1: one webOS TV found in twelve seconds, nothing else) and not necessarily a bug in this build. | ☐ | |
| 2.4 | Select A on B, then **Join**. | B's chat window opens; B's room list matches A's, including `Garden`. | ☐ | |
| 2.5 | On A: the members pane of the room both are in. | `Friend` appears within a second of the join. | ☐ | |
| 2.6 | Close B's Lantern and start it again; **Scan**. | B's scan now lists A even faster, because A is in B's known-hosts list. | ☐ | |

## 3. Chat (scenario 4, over the radio)

| # | Do this | Expect | ✓ | What actually happened |
|---|---|---|---|---|
| 3.1 | On B: join `Garden`. | Both panes show `Friend` joining; a notice appears in `Garden`. | ☐ | |
| 3.2 | A types `hello` and sends. | It appears in B's pane within about a second, with the sender's nickname. | ☐ | |
| 3.3 | B replies. | It appears in A's pane. Neither sees their own line twice. | ☐ | |
| 3.4 | Send twenty messages as fast as possible from both. | All arrive, in the order they were sent, none split or duplicated. | ☐ | |
| 3.5 | A renames `Garden` to `Patio`. | B's list updates without B doing anything, and B stays in the room. | ☐ | |
| 3.6 | On C (if available): join, then all three type at once. | Each of the three sees the other two, and only the other two, in the same room. | ☐ | |
| 3.7 | Walk 10 m away, send, come back, send. | Writes while out of range either arrive late or fail with a message in the status bar — either is acceptable, silence is not. | ☐ | |

## 4. Quit (scenario 6, over the radio)

| # | Do this | Expect | ✓ | What actually happened |
|---|---|---|---|---|
| 4.1 | On B: **Leave** the room and confirm. | A is told Friend left, and the pane loses Friend. Friend is still in the chat. | ☐ | |
| 4.2 | On A: File ▸ **Quit server**, confirm. | A's window closes; B gets a "connection lost" box naming the reason, and B's window steps aside. | ☐ | |
| 4.3 | On A again: nickname `Host`, **Start a Server**; on B: Scan, Join. | Joining again works the same way the first time did. Nothing remembers the dead connection. | ☐ | |
| 4.4 | While B is joined, close the laptop lid on B (or kill Lantern from Task Manager). | A is told within a few seconds and the pane loses Friend. Nothing else is disturbed. | ☐ | |
| 4.5 | Start hosting again on A with B joined, then File > **Quit server** and answer **No**. | Nothing changes: A is still hosting, B is still connected, and chat still works. | ☐ | |

## 5. The things most likely to be flaky, and how to tell them apart

Written down from the spike so that a bad afternoon can be classified while it is happening rather than
guessed at afterwards.

| What you see | Most likely what it is | Write down |
|---|---|---|
| Start a Server refuses, or takes many seconds | The SDP record could not be registered: another program owns the radio, or the adapter went to sleep. Lantern names both in the status bar. | The exact sentence in the status bar |
| Scan finds nothing, twice, though A is online | A is paired but not *discoverable*, which is the normal state of a Windows laptop. The known-hosts list is the design's answer (step 2.6); if that fails too, that is a real bug. | Whether A was ever in the list, and whether it was in the known-hosts list afterwards |
| Join takes about five seconds and then fails | Nothing answered: A asleep, A not hosting, or out of range. Lantern says "did not answer". | Which of those was true |
| Join fails at once about the address | A paired device whose adapter is off, or a driver that will not answer. | The exact sentence |
| Join says Pair | The pairing is not there or not valid on this machine. Re-pair per the how-to. | Whether the pairing dialog was valid on both sides |
| Joined, then lost after a minute of silence | Power management on the adapter: Device Manager → the Bluetooth adapter → Power Management, untick "allow the computer to turn off this device". | Whether ticking it back reproduces the loss |
| Chat works, then a message never arrives | A write that failed while out of range. Lantern reports it in the status bar and keeps the connection. | Whether the connection survived |
| Two machines pair, then a third cannot | Some adapters will not hold three sessions at once. Try A↔B and A↔C without B↔C, which is what the design needs anyway (one host). | The adapter model, and how many sessions it held |

## 6. What to do with the findings

1. Fill in the "what actually happened" column as you go, with times and the exact sentence shown.
2. For every step that needed more than one try, write a line here even if it worked in the end:

   | Step | What happened | How many tries | Error text shown | Flaky / broken / working-as-designed |
   |---|---|---|---|---|
   | | | | | |
   | | | | | |
   | | | | | |

3. Anything broken goes in an issue with: the commit, both machine models and adapters, the step number,
   what the sentence in the window said, and whether a second try changed it. A screenshot of the window is
   worth more than a description of it.
4. Anything working-as-designed-but-annoying goes in the same issue with the word **friction** in the
   title: the field test is the only place those get found.

## Exit criteria for this field test

- [ ] Scenario 1 over the radio, on A
- [ ] Scenario 2 over the radio, A→B by name in the scan, and again via the known-hosts list
- [ ] Scenario 4 over the radio, with twenty messages intact and (if a third laptop exists) three sessions
- [ ] Scenario 6 over the radio, from both ends
- [ ] Every step that needed more than one try recorded in section 6, whether or not it worked in the end
