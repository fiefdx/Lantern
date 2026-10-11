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
| **M7** | The group window, after v1.0 | A transcript that reads as a log; a photo you can actually look at | Week 8 (after the class demo) |
| **M8** | Sharing in both directions | Anything shared can be saved to a folder; a transfer says how far it has got | Week 8 (after the class demo) |

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

- [x] **T4.1 [Net]** Spike: Bluetooth prerequisites on Windows 10/11 — pairing, service GUID registration, one-way vs mutual pairing. Try `System.IO.Ports`-style APIs; if insufficient, adopt 32feet.NET. Write a one-page decision note + a “pairing how-to”.
- [x] **T4.2 [Net]** `BtRfcommServer` (`IBtServer`) and `BtRfcommClient` (`IBtClient`) + `IClientLink` over `NetworkStream`/RFCOMM streams. Keep `FrameCodec` untouched.
- [x] **T4.3 [Net]** Advertise the Lantern service so clients can discover it; real `ServerBrowser.DiscoverAsync(timeout)` returning `ServerEndpoint` list.
- [x] **T4.4 [UI]** `StartupForm` **Scan** → populated server list → **Join** (scenario 2), with a busy indicator and cancel support; status bar shows “scanning…”.
- [x] **T4.5 [Core]** Multi-client host: several simultaneous sessions, per-room member lists verified across 3 machines where possible.
- [x] **T4.6 [Core]** Connection lifecycle: **Quit Server** confirm → `ShutdownAsync()` → clients show “connection lost” (scenario 6); client disconnect → member list + notice updated; reconnect attempt policy (simple: manual re-join).
- [ ] **T4.7 [QA]** Field test on 2–3 physical laptops: pair, host, join, chat, quit. Record which steps are flaky and why.

**Exit criteria:** scenarios 1, 2, 4, 6 run over real Bluetooth between laptops. **F2, F3** covered.

---

## M5 — Pictures, File Transfer & Sounds

**Goal:** the rich-media features on top of the working text chat.

- [x] **T5.1 [UI]** **Share** button: pick image vs file (open-file dialogs); send disabled while in flight.
- [x] **T5.2 [Core/Net]** Picture messages: `PICTURE` frame (JSON header + binary body, no base64), `PictureMessage`, inline thumbnail rendering in the `RichTextBox` (F7).
- [x] **T5.3 [Net]** Chunked file protocol: `FILE_META` → `FILE_CHUNK` (fixed chunk size + sequence) → `FILE_DONE`; driven by `FileTransferJob` with progress + cancel (F8).
- [x] **T5.4 [UI]** Receiver side: incoming-file notice, pick-a-folder dialog, progress indicator per job, save on completion.
- [x] **T5.5 [Net]** `SoundNotifier` (`SoundPlayer`): short sound on new message (F10), distinct sound on file completion.
- [x] **T5.6 [QA]** Tests: chunk reassembly from 1-byte fragments, cancel mid-transfer, receiver picks a read-only folder, a large test file (~10–20 MB) host → 2 clients at once.

**Exit criteria:** scenario 4 with pictures + scenario 5 fully work; sound plays on receive. **F7, F8, F10** covered.

> **As built** — T5.3 needed a thirteenth frame (`FILE_CANCEL`) so a receiver can stop a sender; received bytes
> spool to disk as they arrive rather than sitting in memory; T5.4 is an offer strip and a folder, not a modal
> dialog. Notes: `docs/notes/T5.3-file-transfer.md`, `T5.4-incoming-files-in-the-window.md`,
> `T5.5-sounds-in-the-room.md`, `T5.6-m5-checked.md`; manual sheet `docs/testing/m5-manual-checklist.md`.

---

## M6 — Hardening, Testing & Release

**Goal:** a demo-proof, presentable v1.0.

- [x] **T6.1 [Core]** Edge-case pass: Bluetooth radio off, device walks out of range mid-transfer, duplicate nicknames, host deletes the room a client is typing in, send while disconnected, corrupt frame received.
- [x] **T6.2 [UI]** Status bar states everywhere (server online, connected, reconnecting, connection lost); final UI polish to match wireframes.
- [x] **T6.3 [QA]** Written end-to-end checklist walking through **all six use-case scenarios**; every feature cross-tested by a non-author (contract rule).
- [x] **T6.4 [Ops]** README: install, Bluetooth pairing how-to, feature notes (“how to use” per module), known limitations.
- [x] **T6.5 [Ops]** Tag `v1.0`, prepare the class demo script (one member drives host, others drive clients).

**Exit criteria:** checklist 100% green on at least two laptops; `v1.0` tagged.

> **As built** — `v1.0` is tagged, and the suite is 413 green on 0 warnings. Two departures from the wording,
> both deliberate: **T6.2** has no “reconnecting” state, because Lantern does not dial anybody back on its own
> and the bar must not imply an attempt that is not coming; and the two panes are called Rooms and Members, as
> the proposal and `rooms.json` call them, rather than the Groups and Users the hand-drawn figure labels them.
> The exit criterion is **half met, and the half that is outstanding is a lab, not a bug**: `v1.0`’s tag
> message says so, and the sign-off table in `docs/testing/m6-end-to-end-checklist.md` is empty until someone
> who did not write the code walks it on two physical laptops (the same outstanding sign-off as M4’s field
> test). Notes: `docs/notes/T6.1-edge-case-pass.md`, `T6.2-status-bar-and-figures.md`; sheets
> `docs/testing/m6-end-to-end-checklist.md` and `docs/testing/v1-demo-script.md`.

---

## M7 — The Group Window, After v1.0

Asked for once v1.0 was in people's hands, and all three are one window: the transcript, the picture and the
room list. The wire is untouched, so a v1.1 host still needs a v1.1 client and nothing more.

- [x] **T7.1 [UI]** Message layout (F13): every message gets a **headline** — date, time and sender — on a line
  of its own, and the message itself starts on the line below, in a different colour. Decided once in
  `TranscriptStyle` and painted by `ChatForm`, because the same code draws typed messages, pictures, files,
  join/leave notices and the welcome line nobody sent, and rebuilds the lot from a room's history on every room
  switch.
- [x] **T7.2 [UI]** Picture viewer (F14): double-clicking a photo opens `PictureViewerForm` — most of the screen,
  fitted to it, scalable by buttons, wheel, `+`/`-`/`0`/`F` or double-clicking the photo; the scale read out as a
  percentage; a photo bigger than the window scrolls rather than cropping. One window per photo, owned by the
  chat window. **Save to…** is the only thing that writes a file: looking at a picture used to drop a copy in app
  data and hand it to the system photo viewer.
- [x] **T7.3 [UI]** Room management in the group (F5): Edit gains **New room** (Ctrl+N) and **Delete room**
  (Ctrl+D) beside **Rename room**, host-only on both windows, calling exactly what the header buttons call.
  Creating a room walks the host into it.
- [x] **T7.4 [QA]** Tests: `TranscriptLayoutTests` (the window keeps the layout; colours read back out of the
  `RichTextBox`; a rebuilt transcript is laid out like a live one), `PictureViewerTests` (the scale reaches the
  control that paints; both ends of the range; looking writes no file; one window per photo; closing the chat
  closes the photos; saving writes what it was given), and the room-menu cases in `RoomManagementUiTests`.

> Two things this milestone settled about itself. A double click can no longer be answered from the transcript's
> **text** — an entry is two or three lines now, two photos from one sender in one minute have identical
> headlines, and a file name is in the caption but not in the thumbnail — so each picture's character **span** is
> recorded and a click asks which span it fell in (spans survive word wrap, line numbers do not). And
> `Sta.Run` was reporting success for a UI body that never finished: thirty seconds on a stuck thread and then a
> pass, which is how a test sitting in front of a modal nobody will click looked like a test that worked. It
> throws now. Note: `docs/notes/T7.1-T7.3-the-group-window-grows-up.md`; sheet
> `docs/testing/v1.1-group-window-checklist.md`.

---

## M8 — Sharing in Both Directions

M5 answered how a picture or a file gets into a room. These two are what people found missing once they had used
it: the way back out to their own folder, and any sign that a transfer is still alive.

- [x] **T8.1 [UI]** Saving a shared thing to a local folder (F7, F8): right-clicking a picture's or a file's entry
  in the transcript offers **Save picture to…** / **Save file to…**, asks for a **folder** (the same seam the
  offer strip uses), and hands the bytes over through the same code the offer strip uses. A name already in that
  folder gets a numbered copy rather than being overwritten; asking twice for one file is answered with where it
  already is; a file this window sent is not offered back; an ordinary sentence offers no menu at all.
- [x] **T8.2 [UI]** Progress that answers the question (F8): the line beside the bar names the direction, the
  file, the percentage and the bytes (`receiving notes.txt 34% - 1.7 MB of 5.0 MB`). A **picture** travels in one
  frame, so it has no fraction — it shows a moving bar, its size and the seconds so far, and no Stop button,
  because half a frame is not a state the wire can stop in. Before this, a five megabyte photo over RFCOMM was a
  minute and a half of motionless window.
- [x] **T8.3 [QA]** `SharedThingsTests`: both directions worded and measured, the picture's blind bar and its
  absence of Stop, saving a photo out of the transcript writing what was sent, a second copy taking a second
  name, an answered file still saveable from the transcript later, a sent file and an ordinary sentence offering
  nothing, and one save rather than two for a file asked for twice.

> Two harness facts, both written into CONTRIBUTING. A test that only `await`s does **not** pump the window's
> message queue, so anything a finished operation posts back — its line in the status bar — sits unread, and the
> assertion has to pump the queue first or it fails for a reason that is not the program's. And a fixture sized
> by hope rather than by the property under test (`Png(4000, 3000)`, 48 MB of GDI+, hoping no screen was that
> wide) passed alone and failed in a crowd of five hundred windows; the window is made small now, which is what
> "bigger than the window" actually needs.
- [x] **T8.5b [UI]** (v1.3.1) *Fix*: the right click on that page sometimes did nothing. Three causes, and the one
  everybody suspected first was measured and cleared (a click in the middle of a 40 × 46 page already finds the
  file). A file this window sent now offers **Save a copy to…** instead of refusing in silence; a menu raised where
  the mouse was not asks the caret; and redrawing a room no longer makes the window forget which files it has.
  A right click that is refused says in the bar where to point instead. One question is left open on purpose and
  asked on the checklist: whether a file's entry comes back at all when a room is re-displayed.
- [x] **T8.5 [UI]** A shared file has a picture of its own (F8): a small page (40 × 46) with the file's extension
  written on it, drawn by `FilePlaceholder` rather than shipped as an asset, so a file looks like a file and gives
  the right click something to land on. It sits inside the entry's span, so **right-click the page → Save file to…**
  works from the icon; **double-clicking it opens nothing**, because the picture viewer is for pictures and a file's
  bytes may not exist yet. `FilePlaceholderTests`: the label rules, the drawn pixels, both colours against the
  transcript's white, an embedded page counted in the control's RTF, a right click on the page, a double click that
  opens no viewer, and a picture entry that gets its thumbnail and no page.
- [x] **T8.4 [QA]** Two defects found by using it, both invisible to a green suite. **v1.2.1**: no test had ever
  opened the save menu, so the hit test now forgives a click on the line that ends an entry and falls back to the
  caret when the mouse is not over the text. **v1.2.2**: `PictureViewerForm` had **never displayed a photo** — it
  fitted, scaled, centred and reported a percentage over an empty panel, because `picImage.Image` was never
  assigned in any commit (`git log -S` proves it). Two lines fix it; four tests keep it fixed, one of them
  sampling a pixel of the drawn window.

> Note: `docs/notes/sharing-in-and-out.md` and `docs/notes/the-photo-nobody-could-see.md`; sheet
> `docs/testing/v1.2-sharing-checklist.md`.

---

## M9 — Emoji in What People Type (v1.4)

- [x] **T9.1 [Core]** A message is measured in the characters somebody counts. `ValidationRules.Length` counts text
  elements, and `NicknameError`, `RoomNameError` and `MessageError` use it. A face is two UTF-16 units, four UTF-8
  bytes and twelve JSON-escaped characters; a person counts one, and a refusal whose number cannot be checked by the
  person reading it teaches them the program is arbitrary. 251 faces used to be refused as a 502-character message;
  a nickname of nine faces used to be 18 characters. A joined family and a flag count once each, as they look.
  **The wire did not change**: a frame has always counted bytes of UTF-8, which is what a radio carries.
- [x] **T9.2 [UI]** Emoji are shown, and that was measured rather than assumed. A RichTextBox is a GDI control with
  no colour-font machinery, so whether it put a glyph there at all was found out by drawing the window and sampling
  it: it does, through Windows' font fallback, and naming `Segoe UI Emoji` on the run changes not one pixel — no
  rendering change was needed, only the ruler. `WhatWasTypedWithEmojiIsSentAndShownAsItWasTyped` sends `cà phê 😀🎉` the
  way the picker (Win + .) and a paste deliver it. `AnEmojiNicknameIsShownInTheMemberList` covers the other place a
  person may choose to be an emoji.
- [x] **T9.3 [QA]** `EmojiTests` (16), one framing test and two over real loopback sockets. The drawing test has a
  partner on purpose: ink in an emoji's cell is also true of a row of empty boxes, so
  `TheEmojiIsNotDrawnAsTheBoxWindowsUsesForWhatItCannotDraw` compares the cell with a private-use code point nothing
  has a glyph for. `FiveHundredEmojiArriveWhole` sends 500 characters / 1000 UTF-16 units / 2000 bytes across
  sockets. Sheet `docs/testing/v1.4-emoji-checklist.md`; note `docs/notes/counting-what-somebody-counts.md`. Colour
  or outline is step 2, and is not claimed by any test.

---

## M10 — Colour Where the Faces Are Read (v1.5)

- [x] **T10.1 [UI]** The transcript stops asking GDI for something GDI cannot give. Measured first:
  `GetGlyphOutline` on Segoe UI Emoji for `U+1F600` returns 0 bytes for GRAY8, COMPOSITABLE and INCREMENTAL alike —
  WinForms draws text with GDI, and GDI has no colour-glyph path, so v1.4's outline faces were the ceiling of the
  control rather than a setting. `ColorEmoji` rasterises one text element at a time from
  `C:\Windows\Fonts\seguiemj.ttf` (the font the Win + . picker itself reads, so the artwork is the artwork that was
  chosen) and inserts it as one of the inline pictures the transcript already carried since v1.1. One picture per
  element — two faces in one square cell puts the second outside the bitmap — and all-or-nothing per run, because
  half a sentence in colour reads as a fault. SkiaSharp is the project's first graphics dependency: the alternatives
  were hand-written D2D/DWrite vtables (crash-shaped) or a shipped PNG set (different artwork, several megabytes).
- [x] **T10.2 [QA]** `ColorEmojiTests` (18): the font read is Windows' own; a face comes out with hue in it and more
  than eight shades (`ink` alone is true of an outline, `many colours` alone of a grey photograph); `ê` and `ế` are
  not emoji, so a Vietnamese sentence stays words; a family and a flag are one picture; two faces are two pictures;
  the same face twice is one raster; a body emoji reaches the transcript as one embedded picture at line height; a
  headline emoji is still text; a file's page and a face are two different sizes of picture, so v1.3.1 hit-testing
  survives a text stream that now contains images. The two v1.4 tests that looked at an emoji's cell were
  **re-expressed rather than deleted** — a body emoji has no cell in the control's text any more — and now measure
  the path where an emoji really is a glyph: a headline, the box, and any machine with no colour renderer.
- [x] **T10.3 [Docs]** The cost is stated where it can be read, not discovered: a colourful face is a picture, so
  copying the transcript out carries the words and not the faces, and **the box stays outline** because an edit
  control cannot contain an image and replacing it is a decision rather than a tweak. Sheet
  `docs/testing/v1.5-colour-emoji-checklist.md`; note `docs/notes/the-face-that-came-out-in-colour.md`.

---

## M11 — Choosing an Emoji Without the Operating System (v1.6)

- [x] **T11.1 [UI]** **Emoji** in the chat window opens a table of faces of Lantern's own. The report was "Win + .
  does not pop up"; Lantern cannot be the reason - the shell takes Win combinations before an application is told
  anything - and the machine was checked anyway (text-input service running, both panel processes running, no policy
  on the hotkey, app not elevated, Windows 10 22H2). Which left the feature dependent on somebody else's program, and
  unreachable at all over a remote desktop. `EmojiPickerForm` is a `TableLayoutPanel` of 48 tiles, each drawn by
  v1.5's `ColorEmoji`, one tile per text element; modeless and owned, so the room stays usable while it is down.
- [x] **T11.2 [QA]** `EmojiPickerTests` (10): the button and its shut-when-there-is-no-room rule; the table is owned
  and visible; it is a table (columns), and everything it offers is one character by the ruler and an emoji by the
  renderer, so it can neither offer half a family nor offer a letter as a picture; a tile is a coloured face; a pick
  lands at the caret and the caret is left after it; a pick sends like anything else and arrives in colour; **the
  table refuses to overflow the message and says so**, because assigning to `SelectedText` walks straight past
  `MaxLength` (which only ever guards keystrokes); closing changes nothing.
  One real bug was found by the last of those: a second pick replaced the first, because bringing the message box
  back from the closing picker highlights what it holds. The caret is now said to the window, not left to focus.
  `DesignViewTests` grew a window, as it is written to do.

---

## Feature → Milestone Traceability

| Feature | Milestone | | Feature | Milestone |
|:--|:--|:-|:--|:--|
| F1 WinForms app (.NET 10) | M0 | | F7 Text & picture chat | M2 (text), M5 (picture) |
| F2 Bluetooth networking | M4 | | F8 File transfer | M5 |
| F3 Server discovery | M4 | | F9 Room persistence (rooms.json) | M3 |
| F4 Nickname | M0/M2 | | F10 Sound notification | M5 |
| F5 Room management (host) | M3, M7 (menu) | | F11 Join/leave notices | M2 |
| F6 Room list & switching | M2–M3 | | F12 Room member list | M2 |
| F13 Message layout (headline over content) | M7 | | F14 Picture viewer with scaling | M7 |
| F7 Pictures — saved out to a folder | M5, M8 | | F8 File transfer — progress, saved afterwards | M5, M8 |

---

## Parallel Work & Dependencies

- **UI track** (M0 forms/dialogs → M2.7 → M3.4/3.5 → M5.1/5.4) can proceed against `FakeChatService` before the real engine exists.
- **Core track** (M1 → M2.4/2.5 → M3.3 → M4.5/4.6) is the critical path — start M1 as early as possible.
- **Net track** (T2.1–2.3 codec/loopback → M4 transport → M5.3 chunking) is independent of the UI thanks to the interfaces.
- **Data track** (M3.1/3.2) is small and can run in parallel with anything.
- Highest-risk items: **T4.1–T4.3** (Bluetooth on real hardware) and **T2.1** (framing). Mitigation: the loopback transport in M2 means the project is fully demoable even if Bluetooth slips; keep the loopback build alive as a fallback demo.
- Dependencies at a glance: M0 → M1 → **M2** → M3 → M4 → M5 → M6, except the transport swap in M4, which only replaces M2's loopback implementation behind the same interfaces.
