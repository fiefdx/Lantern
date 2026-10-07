# Lantern — Implementation Tasks & Milestones

Breakdown of the Lantern project proposal into step-by-step work.
No task assignments: any member can pick up any task; work items are tagged
only by area — **[UI]**, **[Core]**, **[Data]**, **[Net]**, **[QA]**, **[Ops]**.

Reference documents: `Lantern_Project_Proposal.md` (features F1–F12, use cases 1–6,
wireframes, class diagrams in Figures 6 and 7).

---

## Definition of Done (from the group contract)

A task is done only when it:

1. compiles without warnings,
2. runs from the current `main` branch,
3. has been tested by someone who did **not** write it,
4. has a short note explaining how to use it.

---

## Milestone Overview

| Milestone | Theme | Demo at the end | Suggested time |
|:--|:--|:--|:--|
| **M0** | Project skeleton & app shell | App starts, both windows open with dummy data | Week 1 |
| **M1** | Domain model & validation | Unit tests green for domain classes | Week 1–2 |
| **M2** | Chat engine + wire protocol (loopback, no Bluetooth yet) | Two app instances chat in a room | Week 2–3 |
| **M3** | Room persistence & room management | Rooms survive a restart; live room list updates | Week 3–4 |
| **M4** | Real Bluetooth transport | Host + client chat over real Bluetooth | Week 4–5 |
| **M5** | Pictures, file transfer, sounds | Picture inline + file with progress + sound | Week 5–6 |
| **M6** | Hardening, testing, release | Full walkthrough of use cases 1–6, tag `v1.0` | Week 6–7 |

**Key strategy:** the transport sits behind `IBtServer` / `IBtClient` /
`IClientLink`, and the UI only talks to `IChatService`. This lets us build and
test the whole chat engine over a **loopback (TCP/in-memory) transport first**
(M2), and only later swap in real Bluetooth (M4) without touching the UI or
core logic.

---

## M0 — Project Skeleton & App Shell

**Goal:** a compiling, runnable, clickable app with no real logic.

- [x] **T0.1 [Ops]** Git repo hygiene: `main`/feature-branch rules, `.gitignore` (VS/.NET), issue board, "one owner + one backup per module" note. *(repo: github.com/fiefdx/Lantern)*
- [x] **T0.2 [Ops]** Solution skeleton (F1): `Lantern` WinForms project targeting **.NET 10**, folder layout `UI/`, `Core/`, `Domain/`, `Storage/`, `Transport/`; plus `Lantern.Tests` unit-test project.
- [x] **T0.3 [Net]** Add NuGet packages (32feet.NET as Bluetooth fallback; System.Text.Json is built in). Record the System.IO.Ports-vs-32feet decision as a short note.
- [x] **T0.4 [UI]** `StartupForm` per Figure 2: nickname box, **Host a chat** / **Scan** / **Join** buttons, server list, status bar. Buttons validated but not yet functional.
- [x] **T0.5 [UI]** `ChatForm` base + `HostForm` (adds **New**/**Delete**) + `ClientForm` (adds **Leave**) per Figures 3–4: rooms list, chat pane (`RichTextBox`), members list, message box, **Send**/**Share**. Note: use the word **“Room”** everywhere (wireframes say “Groups”).
- [x] **T0.6 [UI]** Dialogs per Figure 5: `NewRoomDialog` (room name input), confirm dialogs for **Quit Server**, **Delete Room**, **Leave Room** — all OK/Cancel.
- [x] **T0.7 [Core]** Interfaces from the design: `IChatService`, `IRoomStore`, `IBtServer`, `IBtClient`, `IClientLink`. Add `FakeChatService` (in-memory rooms/messages) so the UI can be developed and demoed without any networking.

**Exit criteria:** `main` builds clean; startup form validates a nickname and opens the right window wired to `FakeChatService` dummy data.

---

## M1 — Domain Model & Validation

**Goal:** the domain classes of Figure 7 exist, are tested, and are used by the fake service.

- [x] **T1.1 [Core]** `Message` (abstract: Id, SenderNickname, SentAt, RoomName, Kind) + `TextMessage`, `PictureMessage`, `FileMessage`, `SystemMessage`, `MessageKind` enum.
- [x] **T1.2 [Core]** `User` (ConnectionId, Nickname) and `Room` (Name, `ObservableCollection<User>` members, in-memory transcript only — never persisted).
- [x] **T1.3 [Core]** `FileTransferJob` (Id, FileName, SizeBytes, BytesDone, `IProgress<double>`, `CancellationTokenSource`).
- [x] **T1.4 [Core]** Validation rules: non-empty nickname (length limits), room name non-empty + unique, non-empty message, sensible max picture size.
- [x] **T1.5 [QA]** Unit tests for T1.1–T1.4. Replace `FakeChatService` internals with the real domain classes.

**Exit criteria:** test suite green; UI still works against the domain-backed fake service.

---

## M2 — Chat Engine & Wire Protocol (no Bluetooth yet)

**Goal:** a complete chat engine over a **loopback transport** (localhost TCP), which proves the protocol and threading before Bluetooth enters the picture. This is the hardest milestone — start it early.

- [x] **T2.1 [Net]** `Frame`, `FrameType` (HELLO, HELLO_ACK, ROOM_LIST, JOIN_ROOM, TEXT, PICTURE, FILE_META, FILE_CHUNK, FILE_DONE, MEMBER_LIST, SYSTEM_NOTICE, ERROR) and `FrameCodec`: 4-byte length prefix + UTF-8 JSON header + optional binary body.
- [x] **T2.2 [QA]** Codec tests that specifically cover RFCOMM stream quirks: frames split across reads, two frames in one read, empty body, large body.
- [x] **T2.3 [Net]** `IClientLink` implementation over localhost TCP + `BtLoopbackServer` (`IBtServer`) with an accept loop.
- [x] **T2.4 [Core]** `ChatServer`: accept loop, `ClientSession` per connection (read loop + write queue), HELLO handshake carrying the nickname, sessions dictionary, join/leave a room, `BroadcastAsync(room, msg)` to members only, join/leave `SystemMessage` notices (F11), member-list update per room (F12).
- [x] **T2.5 [Core]** `ChatClient`: `ConnectAsync`, background `ReadLoopAsync`, `JoinRoomAsync`, `SendAsync` (text only for now), `ConnectionLost` event.
- [x] **T2.6 [Core]** `ServerBrowser` + `ServerEndpoint` (name, address, service id) — loopback version: fixed list or TCP probe.
- [x] **T2.7 [UI]** Bind `ChatForm` to real `IChatService` events (`MessageReceived`, `RoomsChanged`, `MembersChanged`, `StatusChanged`, `ConnectionLost`); marshal to UI thread via `RunOnUiThread` (`Control.Invoke` / `IProgress`); disable **Send**/**Share** while a send is in flight.
- [x] **T2.8 [QA]** End-to-end test: two app instances on one machine (host + client) exchange text messages in a room; notices and member lists correct.

**Exit criteria:** use-case scenario 4 (text-only chat) works over loopback with 2+ instances; **F3–F7 (text), F11, F12** functionally covered.

---

## M3 — Room Persistence & Room Management

**Goal:** the host manages rooms and the room list survives restarts.

- [x] **T3.1 [Data]** `RoomDto` + `JsonRoomStore.Load()` from `rooms.json` on the host machine; missing or corrupt file → default room list, never crash (F9).
- [x] **T3.2 [Data]** `SaveAsync(rooms)` called right after every room CRUD; save runs in background so the UI stays responsive.
- [x] **T3.3 [Core]** `ChatServer.CreateRoomAsync` / `DeleteRoomAsync` (host only) + `PersistAndBroadcastRooms()`; handle a client currently inside a deleted room (kicked with a system notice).
- [x] **T3.4 [UI]** `HostForm`: **New** (→ `NewRoomDialog`), rename, **Delete** (→ confirm dialog) — scenario 3.
- [x] **T3.5 [UI]** `ClientForm`: read-only room list, click-to-join / switch rooms (one room at a time, F6), **Leave** with confirm dialog.
- [x] **T3.6 [Net]** ROOM_LIST frame broadcast so clients update their lists live while the host edits rooms.
- [x] **T3.7 [QA]** Tests: JSON round-trip, corrupt/missing file, duplicate-name rejection, client kicked from deleted room.

**Exit criteria:** scenario 1 + scenario 3 fully work; rooms reload after app restart. **F5, F9** covered.

---

## M4 — Real Bluetooth Transport

**Goal:** replace the loopback transport with real RFCOMM, without changing UI or core.

- [ ] **T4.1 [Net]** Spike: Bluetooth prerequisites on Windows 10/11 — pairing, service GUID registration, one-way vs mutual pairing. Try `System.IO.Ports`-style APIs; if insufficient, adopt 32feet.NET. Write a one-page decision note + a “pairing how-to”.
- [ ] **T4.2 [Net]** `BtRfcommServer` (`IBtServer`) and `BtRfcommClient` (`IBtClient`) + `IClientLink` over `NetworkStream`/RFCOMM streams. Keep `FrameCodec` untouched.
- [ ] **T4.3 [Net]** Advertise the Lantern service so clients can discover it; real `ServerBrowser.DiscoverAsync(timeout)` returning `ServerEndpoint` list.
- [ ] **T4.4 [UI]** `StartupForm` **Scan** → populated server list → **Join** (scenario 2), with a busy indicator and cancel support; status bar shows “scanning…”.
- [ ] **T4.5 [Core]** Multi-client host: several simultaneous sessions, per-room member lists verified across 3 machines where possible.
- [ ] **T4.6 [Core]** Connection lifecycle: **Quit Server** confirm → `ShutdownAsync()` → clients show “connection lost” (scenario 6); client disconnect → member list + notice updated; reconnect attempt policy (simple: manual re-join).
- [ ] **T4.7 [QA]** Field test on 2–3 physical laptops: pair, host, join, chat, quit. Record which steps are flaky and why.

**Exit criteria:** scenarios 1, 2, 4, 6 run over real Bluetooth between laptops. **F2, F3** covered.

---

## M5 — Pictures, File Transfer & Sounds

**Goal:** the rich-media features on top of the working text chat.

- [ ] **T5.1 [UI]** **Share** button: pick image vs file (open-file dialogs); send disabled while in flight.
- [ ] **T5.2 [Core/Net]** Picture messages: `PICTURE` frame (JSON header + binary body, no base64), `PictureMessage`, inline thumbnail rendering in the `RichTextBox` (F7).
- [ ] **T5.3 [Net]** Chunked file protocol: `FILE_META` → `FILE_CHUNK` (fixed chunk size + sequence) → `FILE_DONE`; driven by `FileTransferJob` with progress + cancel (F8).
- [ ] **T5.4 [UI]** Receiver side: incoming-file notice, pick-a-folder dialog, progress indicator per job, save on completion.
- [ ] **T5.5 [Net]** `SoundNotifier` (`SoundPlayer`): short sound on new message (F10), distinct sound on file completion.
- [ ] **T5.6 [QA]** Tests: chunk reassembly from 1-byte fragments, cancel mid-transfer, receiver picks a read-only folder, a large test file (~10–20 MB) host → 2 clients at once.

**Exit criteria:** scenario 4 with pictures + scenario 5 fully work; sound plays on receive. **F7, F8, F10** covered.

---

## M6 — Hardening, Testing & Release

**Goal:** a demo-proof, presentable v1.0.

- [ ] **T6.1 [Core]** Edge-case pass: Bluetooth radio off, device walks out of range mid-transfer, duplicate nicknames, host deletes the room a client is typing in, send while disconnected, corrupt frame received.
- [ ] **T6.2 [UI]** Status bar states everywhere (server online, connected, reconnecting, connection lost); final UI polish to match wireframes.
- [ ] **T6.3 [QA]** Written end-to-end checklist walking through **all six use-case scenarios**; every feature cross-tested by a non-author (contract rule).
- [ ] **T6.4 [Ops]** README: install, Bluetooth pairing how-to, feature notes (“how to use” per module), known limitations.
- [ ] **T6.5 [Ops]** Tag `v1.0`, prepare the class demo script (one member drives host, others drive clients).

**Exit criteria:** checklist 100% green on at least two laptops; `v1.0` tagged.

---

## Feature → Milestone Traceability

| Feature | Milestone | | Feature | Milestone |
|:--|:--|:-|:--|:--|
| F1 WinForms app (.NET 10) | M0 | | F7 Text & picture chat | M2 (text), M5 (picture) |
| F2 Bluetooth networking | M4 | | F8 File transfer | M5 |
| F3 Server discovery | M4 | | F9 Room persistence (rooms.json) | M3 |
| F4 Nickname | M0/M2 | | F10 Sound notification | M5 |
| F5 Room management (host) | M3 | | F11 Join/leave notices | M2 |
| F6 Room list & switching | M2–M3 | | F12 Room member list | M2 |

---

## Parallel Work & Dependencies

- **UI track** (M0 forms/dialogs → M2.7 → M3.4/3.5 → M5.1/5.4) can proceed against `FakeChatService` before the real engine exists.
- **Core track** (M1 → M2.4/2.5 → M3.3 → M4.5/4.6) is the critical path — start M1 as early as possible.
- **Net track** (T2.1–2.3 codec/loopback → M4 transport → M5.3 chunking) is independent of the UI thanks to the interfaces.
- **Data track** (M3.1/3.2) is small and can run in parallel with anything.
- Highest-risk items: **T4.1–T4.3** (Bluetooth on real hardware) and **T2.1** (framing). Mitigation: the loopback transport in M2 means the project is fully demoable even if Bluetooth slips; keep the loopback build alive as a fallback demo.
- Dependencies at a glance: M0 → M1 → **M2** → M3 → M4 → M5 → M6, except the transport swap in M4, which only replaces M2's loopback implementation behind the same interfaces.
