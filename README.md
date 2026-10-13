# Lantern

A local Bluetooth group chat for Windows — C# / Windows Forms / .NET 10.

One machine **hosts** a chat, the others **scan** for it and join as clients. The host organises the chat into
named **rooms**; everyone in a room talks, sends pictures and shares files with each other. No accounts, no
Wi‑Fi, no internet, no server in the cloud — just the laptops in the room and the radios in them.

**v1.4.** 559 automated tests, and a written end-to-end checklist for the parts only hardware can prove
([docs/testing/m6-end-to-end-checklist.md](docs/testing/m6-end-to-end-checklist.md)). v1.1 is the group window
grown up: a transcript that reads as a log, a photo you can look at and scale, and rooms made and unmade from
inside the chat ([docs/testing/v1.1-group-window-checklist.md](docs/testing/v1.1-group-window-checklist.md)).
v1.2 is the other direction: anything shared in a room can be saved to a folder of yours, and a transfer says
which way it is going and how much of it has gone
([docs/testing/v1.2-sharing-checklist.md](docs/testing/v1.2-sharing-checklist.md)).

---

## 1. Install

### Build from source (what the class will do)

```powershell
git clone <this repo> ; cd Lantern
dotnet build Lantern.sln                # 0 warnings is a group rule, so read this line
dotnet test  Lantern.sln                # 559 tests
dotnet run --project src/Lantern/Lantern.csproj
```

Needs **.NET 10 SDK**, or Visual Studio 2022+ with the *".NET desktop development"* workload. Windows 10 or 11.

### One exe to take to the lab

```powershell
dotnet publish src/Lantern/Lantern.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

That leaves `Lantern.exe` (plus the two `.wav` files beside it, which are the message and file sounds) in
`bin\Release\net10.0-windows\win-x64\publish\`. Copy the folder to each laptop — no SDK needed on them. There
is no installer: Lantern is a folder you run, it does not register anything, and it puts its two files (below) in
`%LOCALAPPDATA%\Lantern\`.

### On a machine with no Bluetooth, or one laptop doing both ends

```powershell
Lantern.exe --loopback             # or: set LANTERN_TRANSPORT=loopback
```

Loopback runs the same engine over localhost TCP (ports from 43797 up) instead of the radio, so two windows on
one laptop can be host and client. **One machine cannot be both ends of a Bluetooth link**, which is why this
switch exists. The status bar always says which transport a window is on — read it before blaming the radio.

## 2. Pair the machines first

Lantern only ever dials machines Windows already knows. It does not pair, prompt, or discover unpaired devices,
so a machine missing from the scan list is nearly always a pairing problem, not a Lantern problem.

The long version, with the screens and the error codes, is
[docs/testing/bluetooth-pairing-how-to.md](docs/testing/bluetooth-pairing-how-to.md). In short:

1. Windows Settings → **Bluetooth & devices** → turn Bluetooth **on** on both laptops.
2. **Add device → Bluetooth** on one of them, and make the other discoverable (its Bluetooth page has the toggle).
3. When the two find each other, accept the pairing prompt on **both**, and confirm the same code appears on both screens.
4. Check the second machine now reads *"ready"* or *"connected"* in Settings.
5. Then start Lantern and press **Scan** — the machine appears under the name Windows gives it.

If a scan is empty, the status bar says so and the list stays empty; Lantern never invents a machine to fill the
box. Two things to check in that case: is the other machine hosting yet (nothing is advertised until someone
presses *Start a server*), and did step 3 finish on both sides.

## 3. How to use it

**Host a chat.** Type a nickname, press **Start a server**. The status bar says **server online**, and the rooms
from last time are already in the list (`%LOCALAPPDATA%\Lantern\rooms.json`).

**Join a chat.** Type a nickname, press **Scan**, pick the machine, press **Join**. The host's rooms appear on
the left; choose one to be in it.

**Rooms.** Only the host can change them, and the host does it from inside the chat: Edit → **New room**
(Ctrl+N), **Rename room**, **Delete room** (Ctrl+D) — the **New** and **Delete** buttons in the room list's header
do exactly the same thing, because there is one implementation of each. A name that will not do is refused **as
you type it** — the reason appears under the box and **OK stays greyed until the name is usable**, then lights up
(the dialog used to sit greyed however good the name was: `docs/notes/typing-in-the-room-name-dialog.md`). Making
a room walks you into it; deleting the room you are standing in leaves the window nowhere, and saying what to do
next. Every change is saved to disk
immediately — not on quit — and appears in every connected client's list without anyone pressing anything. A
client sees all three greyed out, so no way to ask ends in silence.

**Talk.** Type in the box, press **Send** or Enter. Everyone in the room sees it; you do not see your own words
twice. Only the room you are in sees it.

**How a message reads.** Who and when are on a **headline** of their own — `[2026-03-07 14:05] User 2`, in dark
blue — and what was said starts on the line below, in black on a white chat area:

```
[2026-03-07 14:05] User 2
the part I typed
```

A group chat is read as a log, and a line that carries the name, the time and the text together is a wall where
nothing lines up. Notices (`User 3 - join this chat`) keep the same shape in two greys, because a notice is not
somebody speaking to the room. The date is in the headline because a chat stays open across an afternoon, and
"14:05" cannot answer "was that today?".

**Emoji.** In the message box press **Win + .** (or **Win + ;**) and Windows puts its picker down at the caret;
pasting works too. An emoji counts as **one** character, so 500 of them are a full message and a nickname of
sixteen faces is a legal nickname - the count is what you see, not what UTF-16 stores. They travel over the
Bluetooth link as themselves, and in the transcript they come out **in colour**: a face is drawn from
`C:\Windows\Fonts\seguiemj.ttf` - the same font the picker itself reads - and put in the line as a small picture.
WinForms draws text with GDI, and GDI cannot resolve a colour font's layers at all (asked for a face it returns an
empty glyph), which is why the faces are pictures rather than text and why Lantern carries SkiaSharp for that one
job. In the message box a face is **still the character** — counted as one, deleted as one, sent as itself — and the
colour there is paint over it: the control's own outline glyph is covered with whatever was behind it and Windows' face
is drawn where the glyph was, repainted every time the box paints. That is the only way colour gets into an editable
box without the box ceasing to hold the text, because a picture embedded in an editable RichTextBox leaves a literal
**space** where the face was. A face you have selected keeps the selection's blue, and a box you have clicked out of
keeps no blue at all, because that is what the control draws.
(`docs/notes/the-face-that-came-out-in-colour.md`, `docs/notes/the-glyph-is-the-advance.md`,
`docs/testing/v1.5-colour-emoji-checklist.md`.)

**Pictures.** Press **Share** and pick an image: it goes into the room as a thumbnail (up to 5 MB). Anything
Share is given that is not an image is sent as a file instead.

**Looking at a picture.** **Double-click** anywhere on a photo's entry — headline, caption or thumbnail — and it
opens in a window of its own, most of the screen, fitted to it. Scale it with **−** / **+**, the mouse wheel, or
by double-clicking the photo; **100%** is the photo's own pixels, **Fit** suits it to the window again, and what
you are looking at is read out as a percentage. A photo larger than the window is shown whole and scrolls; a
small one is not invented into bigger. **And there is a photo in it**: the first two releases of this window
scaled, zoomed and reported a percentage over an empty grey panel, because the control that paints was never given
the picture ([docs/notes/the-photo-nobody-could-see.md](docs/notes/the-photo-nobody-could-see.md)). **Save to…** (Ctrl+S) is the only thing that puts a file on your machine —
looking at a picture writes nothing anywhere. The same photo twice is one window, two photos are two windows, and
closing the chat closes the photos.

**Getting a shared thing onto your own machine.** **Right-click** a photo's or a file's entry in the transcript
and choose **Save picture to…** / **Save file to…**, then pick a *folder* — the same rule as an incoming
file: you say where, the thing keeps its own name. This is the way back for a photo you looked at an hour ago,
and for a file whose offer strip was answered, dismissed, or never appeared because you were standing in another
room when it arrived. A folder that already has that name gets a second file with a number on the end rather
than a photo replaced, and asking twice for the same file is answered with where it already is rather than with a
second copy. Right-clicking an ordinary sentence shows no menu, and a file **you** sent is not offered back to
you — it is already on your machine.

**What progress says.** One bar, both directions, and the line beside it answers the two questions a bar is
asked: `receiving notes.txt 34% - 1.7 MB of 5.0 MB`. A **picture** is carried in a single frame, so there is no
half-way point to measure; what can honestly be said is that it is going and for how long, so a photo in flight
shows a moving bar and a count of seconds (`sending sunset.png - 3.2 MB - 12 s`) and no Stop button, because half
of one frame is not a thing the wire can do.

**Files.** Press **Share** and pick a non-image file (up to 512 MB). A file's entry carries a **small page** of
its own in the transcript, with the extension written on it, so a file looks like a file instead of a sentence —
and there is something to point at. **Right-click the page** for **Save file to…**, exactly as the words offer it.
**Double-clicking it opens nothing**: the picture viewer is for pictures, and a file's bytes may still be arriving
or may have been declined, so a viewer on a file would be an empty window again.
A file **you** shared offers **Save a copy to…**, taken from the file it was sent from; if that file has moved
since, the bar names it and says where it was last. Everyone else in the room gets an offer
strip naming the file, its size and who sent it — and can keep typing while they decide. **Save to…** asks for a
*folder*, not a file name: the file keeps its own name inside the folder, which is the only thing that works when
three files arrive in the same minute. **Decline** throws away what has arrived. **Stop** ends a transfer in
progress, at the sender too. Progress is one bar for both directions. If a folder refuses the file, the offer
comes back and another folder can be chosen — the sender does not have to send it again. A file that is only
half there is never quietly saved under its real name.

**Sound.** Two cues, tellable apart with your back turned: a rising two-note for a new message, and a longer
three-note lower down for a file that has arrived and is ready. **Only the window that receives makes the noise** —
nothing plays for your own words — so a sender that stays quiet is not a fault. If Lantern cannot be heard at all,
the status bar says why, once: *no sound: message.wav is not next to Lantern*. The sounds are two files in `Media\`
beside `Lantern.exe`; replacing a file changes the sound without changing the code, and they have to be plain
16-bit PCM, which is all Windows will play. If a machine must be silent, mute it — there is no in-app switch
(`docs/notes/hearing-it-and-seeing-it.md`).

**Leave.** File → **Quit** asks first, then the server shuts down and every client reports **connection lost**.
The room is told when someone enters or leaves (`Cara - join this chat`, `Cara - left this chat`), and the
member list on the right follows. A client's **Leave** button ends their membership without ending their chat.

**Back to the start screen.** Closing a chat window — either role, however it was closed — brings the startup
window back, where another chat can be hosted or joined without starting the program again. The ✕ on the
*start* screen is what quits Lantern. Going back does not wait on the radio: hanging up a Bluetooth channel
takes seconds on the radio's clock, so a window hands the stopping over rather than standing still for it
(`docs/notes/closing-a-chat-window.md`).

## 4. How it is put together, module by module

| Module | What it is | How it behaves |
|:--|:--|:--|
| `UI/` | The three windows (`StartupForm`, `HostForm`, `ClientForm` over the shared `ChatForm`), `PictureViewerForm`, the dialogs, `TranscriptStyle`, the offer strip and the status bar | Talks to nothing but `IChatService`, so every window is testable against `FakeChatService` with no sockets. Layout lives in `*.Designer.cs` files, behaviour in the `.cs` files. All service events are marshalled back on to the window's thread, and a window that throws while listening does not end the connection. Every window opens in design view: a parameterless constructor exists for the design host, and it works only there (`docs/notes/design-view.md`) |
| `Core/` | `ChatServer`, `ChatClient`, `ClientState`, `HostChatService`, `ServerBrowser`, `SoundNotifier`, `FileTransfer`, `IChatService`, `FakeChatService` | The engine: who is in which room, what gets relayed to whom, what is refused and why. Decisions are made synchronously and I/O is async; nothing on this side of the line knows whether the bytes are moving over Bluetooth or localhost |
| `Domain/` | `Message` hierarchy (`TextMessage`, `PictureMessage`, `FileMessage`, `SystemMessage`), `Room`, `User`, `FileTransferJob`, `ValidationRules`, `FileSize`, `Attachments` | What the program talks *about*, with the rules in it — the 16-character nickname, 24-character room name, 500-character message, 5 MB picture — so the same words come out of the frames and out of the transcript |
| `Storage/` | `IRoomStore` / `JsonRoomStore` (`rooms.json`, one `id` per room), `IKnownHosts` / `JsonKnownHostsStore` (`known-hosts.json`), `ReceivedPayloads` | Everything lands in `%LOCALAPPDATA%\Lantern\`. A save is written to `rooms.json.tmp` and then moved onto the file, so a power cut mid-write leaves the previous list intact. Incoming files are spooled to disk as they arrive, not held in memory |
| `Transport/` | `IBtServer` / `IBtClient` / `IClientLink`, `FrameCodec`, the loopback transport, and the real RFCOMM transport with 32feet.NET behind small interfaces | The wire: a 13-frame protocol (`HELLO`, `JOIN_ROOM`, `ROOM_LIST`, `TEXT`, `PICTURE`, `FILE_META`/`FILE_CHUNK`/`FILE_DONE`/`FILE_CANCEL`, `MEMBER_LIST`, `SYSTEM_NOTICE`, `ERROR`), a length-prefixed codec that survives a byte at a time, and 8 KiB file chunks with back-pressure so a fast sender cannot outrun a slow receiver |
| `tools/BtSpike/` | The M4 spike | The two-laptop RFCOMM experiment that decided the transport interfaces before the product depended on them |

Nothing in `UI/` references `Transport/`; nothing in `Domain/` references either. That is why the loopback build
and the Bluetooth build are the same program.

## 5. Known limitations

Said plainly, because a limitation found by a marker is worse than one admitted in the report.

* **A colourful face in the transcript is a picture, not a character.** Selecting the transcript and copying it out
  therefore carries the words but not the faces, and searching the control's text cannot find a face either. What was
  typed is stored and sent whole, so the other machines still receive the emoji itself. Colour in the **box** would
  mean replacing the edit control, and the box is where the picker puts a face; that was not done, so a face is
  colourful where it is read and outline where it is written. On a machine without Segoe UI Emoji, or without the
  colour rasteriser working, everything falls back to the outline glyph rather than losing the message
  (`docs/notes/the-face-that-came-out-in-colour.md`, `docs/notes/counting-what-somebody-counts.md`).

* **No automatic reconnection.** If the link goes, the window says so and stops. Lantern does not dial anybody
  back: a program that quietly keeps retrying a machine that walked out of range is a program whose user cannot
  tell whether they are in the chat. Joining again is a person's decision.
* **The host is a single point of failure.** When the host's laptop closes, the chat closes; there is no host
  election and no hand-over. Rooms survive on disk and clients rejoin after a restart.
* **One room at a time per person**, and one host per chat. No private messages, no broadcast to all rooms.
* **Nicknames are not unique**, and that is deliberate: a name is chosen once on the way in and cannot be
  changed from inside a room, so refusing a second "Alex" would strand somebody outside with nothing they could
  do. Both get in and the room is told there are now two of that name.
* **A transfer cannot be resumed.** A stopped, declined or interrupted file is deleted and has to be sent again
  from the start. Interrupted *uploads* also leave nothing behind on purpose — a half file under its real name is
  worse than none.
* **A killed process can leave a spool.** Bytes of a file in progress are kept in `%LOCALAPPDATA%\Lantern\incoming\`
  as `*.part` while they arrive. A clean exit and a lost connection both clear them; pulling the power mid-transfer
  can leave one there to be deleted by hand.
* **Sizes are capped**: 5 MB per picture, 512 MB per file, 500 characters per message. A file bigger than the cap
  is refused where it was picked, not halfway across the room.
* **Range and rate are the radio's, not ours.** RFCOMM is a few tens of metres and a few hundred kilobits a
  second; a dozen megabytes is minutes, not seconds, and walking away ends the transfer.
* **Pairing is Windows' job.** Lantern will not pair, prompt, or repair a broken pairing.
* **No encryption or authentication of Lantern's own.** Whatever Bluetooth provides is what there is; a nearby
  machine that is paired and knows the service GUID can join a room. This is a class chat, not a secure channel.
* **Windows only**, and only tested on Windows 11 laptops with Intel radios. Windows 10 is supported by the code
  and untested by us.
* **Rooms are per host.** `rooms.json` is one host's list; two hosts in one lab do not share or merge rooms.
* **Rooms are identified, not named.** A room has an identity that survives a rename and a restart, and that is
  what the frames and `rooms.json` use; the name is what the list says. It also means **both machines must run
  the same build** — an older client asks for rooms by name and will be told those rooms are not here
  (`docs/notes/room-identity.md`).

## 6. Tests and checklists

```powershell
dotnet test Lantern.sln                       # 559 tests: unit, integration, and WinForms UI smoke tests
```

The UI tests create real windows on an STA thread and drive them with a fake service, so the suite needs no
radio, no pairing and no second machine. `docs/testing/` holds the sheets that only hardware can close:

| Sheet | What it walks through |
|:--|:--|
| [m2-manual-checklist.md](docs/testing/m2-manual-checklist.md) | the engine over loopback |
| [m3-manual-checklist.md](docs/testing/m3-manual-checklist.md) | rooms on disk and on screen |
| [m4-field-test.md](docs/testing/m4-field-test.md) | two laptops and a radio |
| [m5-manual-checklist.md](docs/testing/m5-manual-checklist.md) | pictures, files, sounds |
| [m6-end-to-end-checklist.md](docs/testing/m6-end-to-end-checklist.md) | **the six use-case scenarios, signed off by a non-author** |
| [v1-demo-script.md](docs/testing/v1-demo-script.md) | the eight-minute class demo: who drives what, and the fallback plan |
| [v1.1-group-window-checklist.md](docs/testing/v1.1-group-window-checklist.md) | the v1.1 window work: reading the transcript, looking at a photo, rooms from the menu |

Module notes — what each task was for, what was learned, and what was got wrong first — are in
[docs/notes/](docs/notes/). The specification is [docs/PROJECT_PROPOSAL.md](docs/PROJECT_PROPOSAL.md), the task
list is [docs/TASK_BREAKDOWN.md](docs/TASK_BREAKDOWN.md), the diagrams are in [docs/umls/](docs/umls/), and the
working rules (branch policy, module owners, Definition of Done) are in [CONTRIBUTING.md](CONTRIBUTING.md).

## 7. Milestones

| Milestone | Theme | State |
|:--|:--|:--|
| M0 | Project skeleton & app shell | done |
| M1 | Domain model & validation | done |
| M2 | Chat engine + wire protocol over loopback | done |
| M3 | Room persistence & room management | done |
| M4 | Real Bluetooth transport | done in code; the two-laptop field test (m4, m6 📶 steps) needs the lab booked |
| M5 | Pictures, file transfer, sounds | done |
| M6 | Hardening, testing, release (`v1.0`) | done — the sign-off table in the end-to-end sheet is the last thing to fill |
| M7 | The group window, after v1.0 (`v1.1`) | done over loopback; the two-laptop checks of the same window are on the v1.1 sheet |
| M8 | Sharing in both directions (`v1.2`) | done over loopback; saving a shared thing to a folder and watching a transfer go are on the v1.2 sheet |
