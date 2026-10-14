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

## M12 — Formatting Across the Link (v1.7)

- [x] **T12.1 [Core, Transport]** **Formatting rides beside the sentence, never inside it.** `Body` stays exactly
      the string it always was — same bytes, same length, same thing a v1.6 peer puts on screen — and what the line
      looks like travels next to it as `styles`: runs laid end to end over the whole message, each a length plus
      whichever of bold, italic, underline, size (`0`/`1`/`2`) and `#RRGGBB` colour were asked for. Nothing said is
      sent twice, so a plain line gains no field at all and an old peer never sees one.
      `StyledRun` is the run and `FrameHeader.Styles` the wire field; `Frame.Text(..., styles)` and
      `Frame.TextStyles()` are the one place per direction that a domain message becomes a header and a header
      becomes one — which matters because the host does not forward a client's frame, it rebuilds the message from
      the frame it received, and a rebuild that reads only the sentence is where formatting would have silently
      stopped at the host's own machine.
      **The one failure mode and its one remedy.** Runs are counted with `ValidationRules.Length`, the ruler since
      v1.4, so a boundary cannot fall inside `👨‍👩‍👧` and half a family cannot be bold; a list whose lengths do not add
      up to the sentence it annotates is **thrown away whole** and the words are shown. Never half a list. Same
      answer for more than 64 runs, for a size nobody could render, for a colour that is not `#RRGGBB`. An unknown
      key anywhere in the header — and inside a run — is ignored and the thing beside it survives, which is how a
      later round adds strike-through without this round losing the bold next to it.
      The accept-or-refuse is applied wherever a line enters a view, in both directions and on both sides
      (`Frame.TextStyles`, `ChatServer.SendText`, `ChatClient`/`HostChatService` keeping their own copies, and
      `FakeChatService`, which is not allowed to be a kinder protocol than the one the windows will meet): a window
      showing formatting that every receiver refused is two windows describing one message differently.
- [x] **T12.1 [QA]** `StyledTextTests` (14) and `StyledTextRelayTests` (4): the sentence byte for byte with and
      without styles, and no style field on a line nobody formatted; runs that do not add up refused whole while the
      preview still says the whole line; a family emoji is one character of style or none, and a list measured in
      UTF-16 units loses every run in the message; 65 runs refused, 64 arrive; three flags on one character come
      back as three, and one flag alone leaves the other two off; size and colour arrive, and normal size is the
      absence of a field; an impossible size or colour goes with the rest of the list; a header full of keys nobody
      in this build has heard of still arrives as the message it arrived as, id and timestamp included; and the fake
      carries a styled line into the room it recorded it in, refusing what the wire refuses.
      On a live link: the host speaks formatted and the client hears it formatted, a styled line crosses the host
      still styled, a line whose runs do not add up reaches the room as words, and the host's own window files the
      plain copy too. **617 tests** total.
- [x] **T12.2 [UI]** **What was typed bold arrives bold.** T12.1 put the formatting on the link; on its own it was a
      secret the two machines told each other, because both windows drew every line in the room's one font and one
      colour. A run is now drawn as the ink it asks for: bold, italic and underline composed into one font mask,
      size taken from the transcript's own size rather than an absolute point count, colour parsed off `#RRGGBB`.
      `StyledRun.Split` cuts the sentence at the run boundaries using the same ruler the length limit uses, so the
      boundary of a run never falls inside `👨‍👩‍👧` and hands the drawing code half a face, and it makes no assumption
      that the list it was handed is well formed — a run describing more characters than exist stops at the end, a
      run describing none is skipped, and whatever is left over is plain, because cutting a sentence up must never
      be able to lose part of it. `TranscriptStyle` decides what small and large *are* in points (the wire says
      "large"; only this file knows what large looks like, which is what lets it change), and an unstyled run asks
      for no font at all rather than a copy of the one already in use.
      **The scaffolding is never styled.** The headline keeps its colour and its size whatever the message under it
      asked for: a join notice has no business being bold, and a nickname that came out big reads as somebody
      shouting their own name. Faces stay faces inside a styled run — the run goes round the picture, it does not
      stop at it — and `SelectionFont` is set on every run rather than only the styled ones, because a RichTextBox
      carries the last thing set into whatever comes next, so a bold line that did not put the plain font back
      would write the rest of the room in bold.
- [x] **T12.2 [QA]** `StyledTranscriptTests` (14): a styled word arrives bold, and the plain word after it does
      not; italic and underline arrive together; `#C00000` arrives and the rest of the line keeps the colour it had;
      large is measurably taller than the line round it and small shorter; a plain line is drawn plain, which is
      the guard against this feature adding formatting nobody asked for; the headline is not styled; a face inside
      a styled run is still one face; a room replayed from its history replays the formatting too; host window and
      client window put the same ink on the screen for one message; and at the domain level the runs are cut where
      the ruler counts them, no piece ever ends between the two halves of a face, and cutting always gives back the
      sentence — including for a run list that describes 100 characters of a 4-character message.
      Every styled fixture goes through one helper that asserts `StyledRun.Fits` before the message is sent, because
      a fixture whose runs did not add up would not fail as "the runs were wrong", it would quietly arrive plain and
      then fail as "the transcript is broken" — which is a long way to go to diagnose an addition, and three
      fixtures did exactly that while this was being written. **631 tests** total.
- [x] **T12.3 [UI]** **The box formats and the tools say where you are.** The message box becomes a `MessageInput`
      (a `RichTextBox` that says what an empty one is for, since `PlaceholderText` is a `TextBox` idea and this
      control has no such thing) and the tools move into a strip **above** it: Bold, Italic, Underline, Size, Colour,
      Emoji, Share — Send stays exactly where it has always been. Moving Emoji and Share up there is the same
      decision as the strip itself: a button beside a text box is an unrelated button that happens to be nearby,
      and a button above it is a tool that does something to it.
      **The box holds the ink and the window holds the intention.** `brush` is what the next character typed will
      look like, and it is kept in step from both directions: clicking a tool moves the brush and the ink, moving
      the caret reads the brush back off the ink. Only the first half leaves the Bold button lit after the caret has
      walked somewhere plain, and a tool that lies about where you are is worse than no tool. The style mask trap
      the design warned about is real, so `InputFormatting` deliberately does the one thing a RichTextBox is too
      slow for — writes the ink **one character at a time**, reading each character's own flags and changing only
      the bit its tool owns. At the size of a message that is nothing, and it cannot lose an italic, cannot disagree
      with a selection that is partly styled, and cannot be defeated by whatever the control decides to do with a
      mask it was handed.
      **Three things the control does not do, so the window does.** `MaxLength` is not enforced by a RichTextBox and
      counts UTF-16 units in any case, so the limit is applied in `TextChanged` in text elements, cut on a character
      boundary so a family emoji is never halved, with a word in the status bar because a sentence that quietly
      stopped is a sentence the person did not write. A paste is words or nothing, and it is caught on the
      keystroke, not on the window message — see the design note, because what was found there is worth reading.
      And a message that was formatted has to *arrive* formatted: the runs are read back off the box against the
      sentence actually being sent, which is the trimmed one, so the offset of the spaces at its front is what keeps
      them lined up, then folded to the 64 the wire carries and sent only if they say anything at all — an ordinary
      message still travels exactly as it did before any of this existed.
      **The brush is wiped by the send it was typed for.** `RichTextBox.Clear()` leaves the caret's formatting
      behind, so a box cleared while bold is a box that types bold; the reset is explicit and has a test.
- [x] **T12.3 [QA]** `StyledComposerTests` (28): the tools are above the box they format and are not in the row the
      send button is in; the box is two lines tall and scrolls when it needs to and not before; a bold word typed
      and sent arrives bold in the room and the word after it does not; a plain message carries no style field at
      all; bold then italic on one selection leaves both on (the mask trap, named); bold off again leaves nothing to
      say; colour and size arrive as they were chosen; emphasis does not outlive the message it was typed for;
      spaces around a message do not knock the runs out of step; more style changes than the wire carries are folded
      and not dropped, and the folded runs still add up to the sentence; Enter sends and Shift+Enter is left to the
      box; the limit counts characters somebody counts, including a 500th face, and says what it cut off; a paste
      arrives as text and a paste of nothing says so; **a window opening pastes nothing** (the RichEdit window sends
      itself the paste message three times while it is being created, and believing it filled the box off the
      clipboard); Ctrl+V is handled here; the tools show what the caret is sitting in, are greyed out with the box
      when there is nowhere to send, and are drawn in the size and colour they are about to give. Ten existing
      `(TextBox)` casts on `txtMessage` and one wireframe-parity test of the input row moved with the control.
      **659 tests** total.
- [x] **T12.4 [UI]** **Three columns, and each one owns what is inside it.** The window was a flat grid of four
      columns and four rows, which meant the tool strip was told to span the whole of it: half of a row of buttons
      lay over the rooms list and part of it over the members column, with an unexplained empty column between the
      message box and Send. A row of tools with nothing under it reads as a row of tools that does not work. The
      window is now Rooms | chatting area | Members, one row, and the middle column splits into the transcript over
      the input area, which splits into the tools over the box and Send.
      **A pane belongs to exactly one column** — that is the whole change, and it is not cosmetic: the tools are
      *inside* the middle column, so they cannot be over the rooms list even by accident, and the box and Send are
      inside it too, which is what makes them the tools of that box rather than a row of buttons that happen to be
      nearby. Each side column carries its own header now, so the headers belong to what they head and the middle is
      free not to have one. Send stays on the box's row and not on the strip: everything that puts something *into*
      a message is on the strip, and Send is what *ends* one. The transcript is the row that gives — `pnlInput` is
      `AutoSize` over two fixed rows, so a taller window hands the extra room to the conversation rather than
      padding out the input area.
      **What the ruler found, twice.** At the window's minimum width the Emoji button was sitting on top of the
      Underline button: the strip is fixed-width buttons at the left plus right-anchored buttons, and narrowing the
      window walks one group into the other — the failure being that clicking Underline opened the emoji table. The
      widths are now measured to fit at `MinimumSize` with 10px between the groups (Share was 170 wide only because
      it used to fill a 170-wide column; it is the word "Share"). And on a machine whose text metrics came out at 17
      where these numbers were chosen at 15, every tool button was 27 tall inside a 28 strip and the room buttons
      hung 1px out of their header: absolute row heights chosen on one machine's font. The tool row is 34 and the
      headers 30.
- [x] **T12.4 [QA]** `WireframeParityTests` gained four: the three columns by index *and* by edge, with each list
      inside its own column under its own header; the chatting area being the transcript over the input area, and a
      taller window giving its extra height to the transcript; the input area being its toolbar over the box with
      Send beside the box on the box's row, all seven tools on the strip and Send not, and none of it reaching
      outside the middle column; and **`TheToolsDoNotFallOnOneAnotherWhenTheWindowIsAtItsNarrowest`** — no tool off
      either end of the strip and no two tools intersecting, at `MinimumSize`, in screen coordinates. Geometry is
      asserted in one coordinate system because these panels are not siblings, and a comparison of two panels'
      `Left` values means nothing when they have different parents. One older assertion had to be turned the other
      way: Send used to be asserted *not* a sibling of the box, and is now asserted to share the box's row.
      **664 tests** total. Note `docs/notes/the-window-in-three-columns.md`; sheet
      `docs/testing/v1.7-formatting-and-layout-checklist.md` (which also covers T12.2 and T12.3, the v1.7 features
      having had no sheet of their own until now).
- [x] **T12.5 [UI]** **The tools above the box, optimised where a person actually aims.** Size and colour became
      menus: they had been cycles, and a cycle hides five of the six colours behind a click nobody thinks to make
      while leaving no way to see which of the three sizes is in use without clicking round through all of them.
      The menu is the whole list at once — each size written at its own size, each colour written in itself, tick on
      the choice the caret is in — and it is *built from* `ComposerStyle.Sizes` and `ComposerStyle.Palette` rather
      than written out item by item, because a palette listed twice is a palette that can be edited in one place and
      missed in the other, and the disagreement is a menu saying Green over a colour that arrives red. Hence
      `Ink(Name, Color)`: the name and its colour are one entry now. Bold, italic and underline stay one-click
      toggles, being the three that switch. The emoji tool wears Windows' own smile and the share tool a page drawn
      in eight lines of code (`ToolIcons.Page`, taking `SystemColors.ControlText` so it survives a theme nobody has
      looked at); the words moved to `AccessibleName` and the tooltip, which are the two ways of asking that a
      picture cannot answer.
      **What measuring the faces found.** “The emoji is cut off in the toolbar” was not the button. Counting pixels
      in what `ColorEmoji.Render` produces: at an em of N in a cell of N, the ink of 🙂 reached the last row *and*
      the last column at 16, 18, 20, 22, 24 and 28, and 🇻🇳 ran off both sides besides. A COLR/CPAL emoji's ink box
      is not its em box, and the font's metrics describe the typeface rather than one emoji's artwork, so there is
      no baseline offset that fixes it — the ink is taller than the em drawn at, and sliding it moves the cut from
      the chin to the hair. The ink is therefore *measured* now (a probe canvas four times the em, a scan, then a
      scaled and centred draw into the cell), allowing a pixel of air, a pixel of overhang for antialiasing, and
      never scaling up — a flag stays a flag and not a fat flag. The cell stays exactly N×N because the line a face
      sits on is sized by that number. Faces are ~11% smaller and complete, in the toolbar, the picker and the
      transcript at once, since all three ask this one method. `Button` also turned out to have no `DisplayStyle`
      (that is a `ToolStripItem` property) and to cut an over-large image with no complaint of any kind, so the
      picture size is chosen against the button's own client area and the emoji tool falls back to the *word* where
      there is no emoji font — a button with neither picture nor text is a button nobody can find.
- [x] **T12.5 [QA]** `StyledComposerTests` gained nine: the size tool's menu is the three sizes and each is set in
      its own size; the colour tool's menu is the whole palette with every entry's `ForeColor` the colour its name
      claims; the tick follows the caret, not the last click; **Automatic** sends no colour field rather than the hex
      it happens to look like; the emoji tool is a face with no word, reachable as `Emoji`, with its picture no
      bigger than the space showing it; the share tool is a page, named as sharing and hovering as “picture or file”;
      and **`AFaceIsDrawnWholeWhereverItIsDrawn`** — six faces at four sizes, failing if any ink lands within a pixel
      of a cell edge, which is the test that the old renderer could not pass at any size. Menus are found through
      `button.ContextMenuStrip` rather than by reflecting a field, because that is where a menu belongs and it is how
      a right-click finds it; the two tests of the old cycling behaviour were rewritten to choose from the menus.
      **A test that measured the same thing three times.** `TheToolsStillFitWhatTheySayAtTheSizeTheyShow` was first
      written without entering a room — and with nowhere to send, the box is shut, the menu pick does nothing, and
      the size tool's word was measured at 9 points three times in a row while the test reported that large fits.
      It fits with 46 of the 58 pixels it is given, which is the fact the test was after; the first version could not
      have failed. Any UI test that picks something from a menu has to be in a room first, and this one says so in a
      comment where the next person will read it.
      `TheArrowSayingAToolOpensAMenuIsAGlyphTheFontHas` asks the font's own glyph table about U+25BC, since a missing
      glyph comes back as a replacement box roughly the width of the arrow it replaced. One `EmojiPickerTests`
      assertion moved from the button's `Text` to its `AccessibleName`, and the wireframe-parity test of the input
      area's words with it. **673 tests** total. Note `docs/notes/the-face-is-the-button.md`; sheet
      `docs/testing/v1.7-formatting-and-layout-checklist.md` rewritten round the new tools (21 rows, of which 10–11
      and 20 are the ones only a person can settle).
- [x] **T12.6 [UI]** **The face in the box is the coloured one.** The message box drew 🙂 as a black outline while
      the transcript, the picker and the toolbar all showed the coloured face, because a `RichTextBox` paints its
      text with GDI and GDI does not render a COLR/CPAL font's colour layers. Embedding a real picture was measured
      and rejected — in an *editable* RichTextBox an RTF `\pict` leaves a literal space (`0x20`) in `.Text`, so
      `a🙂b` arrives at the room as `a b`: not a worse-looking message, a different message. So the character stays
      and the face is **paint over it** (`FaceOverlay`, called from the control's own paint): the glyph is covered
      with what was behind it and Windows' own artwork is drawn where the glyph was. The text is untouched, so the
      count, the caret, the wire and the 500-character rule never hear about any of this.
      **Four things about this control turned out to be untrue, all of them measured.** `OnPaint` is never called
      for a RichTextBox — one paint message in, zero overrides invoked — which means the placeholder word added in
      T12.3 had **never once appeared on screen**, and its test had been passing all along by asking the `ShowsHint`
      property instead of the window; the overlay is therefore wired to `WM_PAINT` in `WndProc`, and the hint with
      it. `DrawToBitmap` returns almost none of a RichEdit's glyphs (31 dark pixels for a line of text), so nothing
      is asserted from a picture of the control; `CopyFromScreen` works but has to be let settle, since the same
      white paper read `D7D7D9`, `E7E7E8`, `ECECED` on three captures in a row. The square a face is painted into
      comes from the control's own idea of where the character is — its advance, 12 pixels at 9pt — and the erase
      underneath it is an **ellipse**, because a face's footprint is round and a rectangle rubs out the first strokes
      of the letter after it. (The ratio this entry originally claimed for that square — an em a third wider than the
      advance — turned out to be measuring a full stop. See the fix entry below.) And the colour behind a face is
      `SystemColors.Highlight` exactly while the box has the caret and the paper when it has not, because an
      unfocused RichEdit draws no selection at all — hence repaints on focus gain and loss alongside text, caret,
      size and scroll.
- [x] **T12.6 [QA]** `ColorFacesInTheBoxTests`, eleven tests: the box paints a face in Windows' colours and in more
      than one hue (one flat blob would be a stuck brush, not a face); the erase leaves the words beside a face
      alone; **`TheSquareCoversTheGlyphTheControlActuallyDraws`** holds the painted square against the measured ink of
      the control's own outline glyph at four sizes, which is the test a wrongly-sized patch fails outright; a face
      in large words is bigger than one in ordinary ones, and one wrapped onto a second line is
      painted on the second line; a joined family is one face and not three; a sentence with no face in it is left
      untouched and a sentence with one in it is *left alone* — same `Text`, same length, before and after the paint;
      and **`TheBoxPaintsItsOwnFaces`** pins the control's paint to the painter the other ten call, without which
      every measurement above would be about a method nobody runs. **`AnEmptyBoxPaintsTheWordThatSaysWhatItIsFor`**
      is the regression test for the dead `OnPaint`: it asserts `PaintedHint`, which is the same claim made about
      the paint rather than about the text, and it is the reason the placeholder word is on the screen now. A
      selected face asserts both halves of the measurement — the selection colour while the box has the caret, and
      no blue at all when it does not — and says out loud that the first half is worthless without a real focus.
      **684 tests** total. Note `docs/notes/the-glyph-is-the-advance.md`.
- [x] **T12.6 [UI,QA] fixed** — the coloured face showed beside the character rather than on it, which is what the
      task had been opened to prevent, and the cause was a measurement rather than a paint. The square had been
      `advance + advance / 3` starting one pixel inside the cell, on the strength of an earlier reading of 15 pixels
      of ink on a 12 pixel advance. That reading measured `🙂.`: **the full stop was the extra three pixels**, and its
      height dragged the vertical bound down to the baseline with it. Measured again with a face and a run of spaces,
      the caret parked in the spaces (an active RichTextBox puts a blinking caret — a column of ink one line tall —
      into every capture), the glyph's ink is the advance exactly, square, on the cell's left edge, two pixels below
      its top at 7 and 9pt and three at 11 and 13pt. So `CellOf` returns the advance with `dy = (advance + 4) / 6`,
      derived from the run's advance so a face typed Large comes down with its own line.

      The second offset was in the artwork, not the geometry: a bitmap of a face is not a face that fills a bitmap
      (a 15 pixel bitmap inks 12 of them, a 22 pixel one inks 18, and the air measures three pixels at nearly every
      size from 9 to 25), so drawing one into a rectangle puts the picture inside it and low. `CachedGlyph` now
      returns the ink it measured while rasterising alongside the bitmap, and `DrawOf` hangs the drawing from the
      middle of that ink — the one point on the picture that may be placed by the square — at the scale that lands the
      ink on the glyph. Ask for the bitmap at the square plus three and that scale is one exactly: the face is
      rasterised at the size it is shown rather than stretched into it.

      `ThePaintedFaceIsTheSizeOfTheCharacterItCovers` is the regression test, and it is the shape the earlier tests
      were missing: measure what was painted and compare its bounds to the character's square, rather than assert the
      square's arithmetic at one another place. It excludes the erase patch by colour, since on a grey canvas the
      box's own white paper shows as a pale ellipse a pixel wider than the character all round. Measured off the
      screen in two windows apart — an ordinary RichTextBox and the message box, each alone, since one capture of two
      overlapping windows is a capture of whichever is on top — the painted ink and the glyph's ink occupy identical
      rectangles at 7, 9, 11 and 13 points, with zero pixels of anything outside the square in either. **686 tests**.
- [x] **T12.6 [UI,QA] fixed again** - a face no longer stands where its own size would put it, because a line is not
      one size. Reported twice: **in the box**, a Large word with an ordinary face beside it left the face riding near
      the top of the line with its black outline showing underneath; **in the transcript**, a face sent at Large arrived
      at the size of the sentence it travelled in. The transcript half was ours - `WriteContentRun` asked how tall a
      face should be drawn *once for the whole message*, so every face in it came out alike - and it is asked per run
      now, off that run's own font, with a face too tall for its line clipped to the line instead of pushing it open.

      The box half belongs to the control: it lays a line out on one baseline, and answers where a character is with
      the top of the **line** for every character on it whatever size it is. So the offset comes from the tallest ink on
      the line rather than from the face's own advance - an ordinary face beside Large letters stands **8** pixels down,
      not the 2 it stands on alone - and the bottom of ink `em` tall is `em + (em + 4) / 6` (`FaceOverlay.Bottom`; the
      table is in `docs/notes/the-glyph-is-the-advance.md`).

      Getting the tallest ink is the interesting half. A line holding a face never reports one font: `SelectionFont`
      calls a change of *family* a difference, and RichEdit borrows `Segoe UI Symbol` or `Calibri` to draw a face and
      reports what it drew with rather than what it was given - so the size must be read, and reading it is cheap per
      question (0.03-0.09 ms) and ruinous per character (a character-at-a-time walk measured **15 ms** for forty
      characters, and every keystroke throws the answer away). So **faces are measured, not asked about** - their ink is
      their advance, and the advance ends where the next character starts - while the words between them are asked in
      stretches that halve only when they answer "mixed", cut on text-element boundaries. Measured: seventy words, 2
      questions; a face among sixty-three characters, 2 (walking cost 12 and 15 ms); a line of faces alone, none.
      `MessageInput` keeps the answer per display line and forgets it when the text, any font in it, or its own size
      changes; it puts the selection back where it found it and says so through `Measuring`, so a measurement cannot
      wipe a pending format off the toolbar.

      `TwoFacesOnOneLineEndAtTheSameLine` holds two faces of different sizes to one baseline, and
      `FindingABaselineAsksAboutWordsAndNeverAboutFaces` holds the reading to its cost - both are tests that fail on
      the version before. Checked against the control rather than against ourselves: its own glyph and the face painted
      over it both begin 8 pixels below the top of a mixed line, both exactly one em tall, no outline left showing.
      **691 tests**, 0 warnings.

- [x] **T12.6 [UI,QA] fixed again (a second report, two defects)** - the fix above was checked against a still box,
      and a moving one produced two new reports at once: **"it refreshes many times in a flash"** while faces were
      typed next to words of two sizes, and **"the transcript ends up not showing all emoji, some missing, some display
      as others"**. They are unrelated, and each was hiding behind the other.

      **The flash is a loop, not a cost.** Reading a line's tallest ink highlights stretches of it, a highlight makes a
      RichEdit window draw itself again, and this window paints a face over its own outline glyph on every paint message
      it gets - so measuring *from the paint* measured from the paint. `MessageInput.Paints`, counted in `WndProc`
      because a RichEdit raises no paint event and `OnPaint` does not happen, says what one cold reading of a
      twelve-character line cost: **346 selections and 31 paints**, with the trace showing the *same* `Select(2, +12)`
      over and over - the same question asked again from inside itself, since the answer had not been reached when the
      first of those paints arrived. Two changes, in this order, because the first alone is not enough:
      `EM_HIDESELECTION` (`WM_USER + 63`) around the reading, which is worth exactly what it says - **two paints per
      highlight shown, none hidden**, measured - and measuring **when the text, a font in it, or the size changes**
      (`MeasureInk`) instead of when the window next paints, with `TallestEmOnLine` demoted to reading what was found
      and a face with no answer standing on its own em. A measurement that cannot be completed is not made cheap by
      being invisible; the laziness that had been protecting the keystroke was protecting nothing, since re-reading six
      lines including faces costs **1-2 ms**. `FindingABaselineNeverAsksForTheWindowToBePainted` counts paints.

      **Three faces arrived as one because the RTF header was written per picture.** `RtfPicture.Inline` wrapped every
      picture in a complete RTF document, and a stretch of adjacent emoji concatenated those documents into one
      `SelectedRtf` assignment; RichEdit reads *a* document, and `🙂😀👍` came back out of the transcript with **one**
      `\pict` while `🙂 😀 👍` - faces in separate stretches, so separate assignments - correctly had three. That is
      "some missing", and it is why no test had seen it: every face test written until then had a word between its
      faces. `RtfPicture` is `Group` (one `\pict`) and `Document` (one header, N groups) now, and `ColorEmoji.InlineRtf`
      collects the groups of a stretch and asks once.

      `ThreeFacesTogetherAreThreePicturesAndNotOne` failed twice before it was believed, and the second failure was the
      test's own: a RichTextBox does not write a picture's hex back out the way it was put in - it breaks it into
      **lines** (`\pichgoal270`, a line break, then the hex), and the first ~200 hex digits of two different faces are identical because
      they are the PNG magic and the `IHDR`/`sRGB`/`gAMA`/`pHYs` chunks of two 18x18 RGBA files. Comparing a prefix of
      each picture therefore reports "three faces, one artwork" - the report under investigation, manufactured by the
      test. It compares whole blobs now. **693 tests**, 0 warnings.

- [x] **T12.6 [ui,qa] fixed once more (the transcript, not the box)** - "transcript shows mixed size text and emoji
      icon, the emoji icon and the text vertical not aligned". True, and true since colour faces went into the
      transcript: a face there is a picture, and RichEdit puts every picture at **the top of its line** - measured on
      six lines, the same placement whether the line was 14, 18 or 25 pixels tall. A face's ink is 3 pixels of air in
      from the edge of its picture, so every face finished with its chin 15 pixels down while the letters of those
      lines stop at 13, 16 and 23. An ordinary face beside Large words therefore floated **8 pixels**, a small face
      beside ordinary words **6**, and only the case where face and line happen to be the same size - which is nearly
      every message, and every face in every test - looked right.

      Asking where the baseline is was tried and does not work: `FontFamily.GetCellAscent` says 14.03 for the room's
      Segoe UI 9.75pt, GDI's `GetTextMetrics` says `tmAscent` 14 with `tmHeight` 17 for a font whose `Font.Height` is
      18, and GDI `DrawText` puts the ink's last row at 13 from the top of its rectangle. The screen says 16. What
      does hold at every size measured is that **letters stop two pixels above the bottom of `Font.Height`** (16 of
      18, 23 of 25, and 13 of 14 at Small - one off the rule, which is as near as font arithmetic gets at 14 pixels),
      so the picture is made as tall as the line's tallest letters and the ink is set two above the bottom of that.
      The face is never stretched: same picture, same size, standing lower.

      Two details carry the rest. A run that is **nothing but a face does not set a line's height** - measured, a Small
      line with an ordinary face on it was laid out for the Small words - so the box a face stands in comes from the
      runs with words in them. And a face taller than its line cannot stand on it and stay below the top of it, so such
      a face is stood as near as it gets with **its own air clipped rather than its crown** - which is why a Large face
      on an ordinary line hangs 1 pixel instead of 3 and still has a forehead. `AFaceStandsOnTheLineItsWordsAreOn`
      reads both wrong sizes back out of the control's RTF - the height it was told, and where the ink sits inside it -
      and `ALargeFaceIsDrawnLargerThanAnOrdinaryOne`/`ASmallFaceIsDrawnSmallerThanAnOrdinaryOne` were re-expressed to
      measure the ink rather than the picture, because the picture is now the line. **694 tests**, 0 warnings.

- [x] **T11.2 [ui,qa] fixed** - "remove the last row last two emojies". The table's last row ended with a joined
      family and the flag of Vietnam, and they had been put there on purpose: each is one character by the ruler a
      message is counted with, which is exactly what `TheTableIsATableAndEveryTileIsOneCharacter` checks. Counting was
      never the problem. The faces on the tiles are drawn **here**, out of the font's colour tables, one code point at
      a time - and a family is three people plus two zero-width joiners, and a flag is two regional-indicator letters.
      Composition into one face is a font engine's work, so those tiles showed a crowd and two letters. The rule now
      belongs to the table rather than to the two faces: **one code point, or one code point and a variation selector**
      - `TheTableOffersNothingThatIsMoreThanOneFace` asserts it over every entry and names the pair that taught it.
      Reading them is untouched: `IsEmoji` still says yes to a joined family and the transcript still writes one picture
      for one grapheme, because another machine may send one and being unable to read it would be a fault, not a
      policy. **697 tests**, 0 warnings. Note `docs/notes/the-face-that-came-out-in-colour.md`; sheet row 30.

- [x] **fix(ui) [ui,qa] (v1.7)** - "not every time I double click a image shared in the transcript can open the image
      viewer window, sometimes there is no reaction at all". A thumbnail is an inline picture, which is to say **one
      character**, and opening it has always been one question: which character is this click on. Measured on a real
      transcript with a 140 × 90 photo whose character is 158 and whose entry ends at 159: clicks at +2, +35 and +70
      all report 158, and clicks at **+105 and +138 report 159** - one past the end of the entry, which the strict rule
      for opening a picture can only read as "not on the picture". So the left two thirds of a photograph opened the
      viewer and the right third did nothing, and that line is invisible because it is not a line in the photograph, it
      is a line in how the control divides the picture's box. `ADoubleClickInTheMiddleOfAThumbnailOpensTheViewer` has
      been clicking +70 since v1.3.1, which is on the answering side of a boundary nobody knew was there.

      `caret <= span.End` is not the fix: the character after a thumbnail is the line break before the next entry, and
      clicks in the empty band **under** a tall photo report that character too - so the fix would open a photograph
      from the strip of nothing beneath it, the same fault pointed the other way. The question is about pixels, so the
      span now remembers the **box** as well as the characters: the picture's own character (`Cell`, or -1 when there
      was no image to draw) and the size in pixels it was asked to fill - knowable once, when the thumbnail is written,
      because working it out later means decoding the photograph again on the way to showing it. Hence
      `RtfPicture.InlineFor` hands back the RTF *and the size it asked for*. Scaled by `DeviceDpi / 96`, and the hit area
      is the photograph and not its line, which is what `ADoubleClickBelowAThumbnailOpensNothing` holds. The right-click
      menu had the identical hole - **Save picture to…** unreachable from the right third of a photo - invisible only
      because the menu's rule is generous enough to hide it; `PictureAtPixel` now sits behind both.
      **697 tests**, 0 warnings. Note `docs/notes/the-right-third-of-a-photograph.md`; sheet row 31.


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
