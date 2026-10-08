# Lantern v1.0 — class demo script

Milestone M6 · task T6.5 · area Ops

Eight minutes, four people, two laptops minimum. One member drives the **host**; the others drive **clients**.
Everyone else stands where the screen is visible and says nothing while a step is running.

The rule for the whole demo: **the person who wrote a piece of code is not the person who demonstrates it.**
It is the same rule as the QA contract, and it is the reason a demo does not turn into an apology.

## Who

| Role | Drives | Also carries |
|:--|---|---|
| **Host** | the laptop that pressed *Start a server* | the power cable, and the laptop's own name written on paper |
| **Client A** | the second laptop; sends the picture and the big file | a USB stick with `holiday.jpg` (2 MB) and `deck.zip` (12 MB) |
| **Client B** (if there is one) | the third laptop; is the one who declines a file and walks away | — |
| **Narrator** | nothing; reads the script and holds the clicker | this page, printed, and the fallback plan in the last section |

Names used below are the ones typed into the nickname box: the host is **Ana**, the clients are **Ben** and **Cara**.

## The night before

| | Check | Why it is on this list |
|---:|---|---|
| ☐ | Both laptops paired, and the pairing *still there* (Settings says ready) | Pairing is Windows' job and Windows forgets. Lantern dials only machines it already knows |
| ☐ | Both on the **tagged build**: `git describe --tags` says `v1.0` on both | A demo of a working tree is a demo of something that no longer exists |
| ☐ | Radio on, airplane mode off, both laptops **on the same floor** | RFCOMM is tens of metres; the demo walks one laptop away, it does not start there |
| ☐ | Sound audible on the receiving laptop, and the sound scheme not "No Sounds" | F10 is a feature; silent speakers make it an unseen feature |
| ☐ | `holiday.jpg`, `deck.zip` on the Desktop of Client A's laptop, and `%TEMP%\empty` folder created for the file to be saved into | Nobody demos well while browsing a disk |
| ☐ | `%LOCALAPPDATA%\Lantern\incoming\` empty, and `rooms.json` containing exactly the three demo rooms **Project Standup / Design / Off-topic** | The persistence beat is "the rooms came back"; that is not a beat if they were never gone |
| ☐ | One full rehearsal of the eight minutes, out loud, with the actual page | Every demo has one step nobody thought about. Find it the night before |
| ☐ | Rehearsal done once with `--loopback` on one laptop, too | That is the show-with-one-laptop plan, and it has to be a plan rather than an improvisation |

## The demo

**0:00 — One line, no slides.** *"Four laptops, no Wi-Fi, no internet, no accounts. These machines are going to
talk to each other over the radios in them."*

### 1 · Host a chat (0:30) — host types

1. Open Lantern on the host. Point at the transport sentence in the status bar: *"this window already knows
   which radio it is on."*
2. Nickname **Ana**, press **Start a server**.
3. Say the two things that just happened: the bar says **server online**, and the room list is *last Tuesday's*
   rooms — `rooms.json` on disk, not three invented ones.
4. Say who is host: this laptop is the only one that can make rooms, and if it closes, the chat closes. Say it
   now, so the ending at 7:30 lands.

### 2 · Join a chat (1:15) — Client A types, narrator counts

1. Client A: nickname **Ben**, press **Scan**. *"This is a Bluetooth inquiry — the list fills with paired
   machines that are advertising Lantern."*
2. Select the host, press **Join**. Both windows change at once.
3. Point at the host's member list and its transcript line **Ben - join this chat** (F11 and F12, in one
   gesture). Say what is not there: no account, no IP typed by hand, no server anywhere else.

### 3 · Manage rooms (2:00) — host types, everyone looks at Client A

1. Host: **New** → `Project Standup` already exists, so create **Sprint 40**.
2. Immediately look at Client A's room list: *"nobody pressed anything over there."*
3. Host: Edit → **Rename room** → `Sprint 40` becomes `Retro`. Client A is inside it and stays inside it.
4. Host: on a **client**, open the Edit menu and hover the greyed buttons. *"Only the host edits rooms, and the
   program says so instead of waiting for you to find out."*
5. Narrator, one line about disk: create, rename, delete — all in `rooms.json` **immediately**, not on quit.

### 4 · Chat (2:45) — both type

1. Both pick `Retro`. Ben sends *"hi from the other radio"*; Ana sends *"hi back"*.
2. Point out the two things people check for: Ana does not see her own line twice, and Ben's line is
   attributed to Ben in Ana's window.
3. Ana creates nothing — instead select `Off-topic` and Ben follows: *"the other room sees nothing of this."*
   Two messages there, then back to `Retro`: the histories are separate.
4. Someone enters `Off-topic` and leaves: **Cara - join this chat**, **Cara - left this chat**.
5. Ana shares the picture: **Share** → `holiday.jpg`. A thumbnail in both windows; click it and the real file
   opens. The receiving laptop *pings* — that's F10, and there are two different cues: this one is a message.

### 5 · Share a file (4:15) — the beat the demo is really about

1. Ben presses **Share** → `deck.zip` (12 MB). Ana's window gets an **offer strip** — name, size, who is sending.
2. *"Notice the strip is not a dialog: Ana can keep typing while she decides."* Type one message in `Retro`
   while the file is coming, to prove it.
3. Ana presses **Save to…** and picks `%TEMP%\empty`. One bar, running to 100 %. *"It asks for a folder, not a
   file name — the file keeps its own name, which is the only thing that works when three files arrive in the
   same minute."*
4. Different cues: when it lands, Ana hears the **file-ready** sound without looking at the screen. Say it:
   *"that was a different sound, and it means the file is finished and somewhere I chose."*
5. If there is a Client B: Ben sends again; **Cara declines**. Point at Ben's side: *"her Stop was also his
   Stop — he is not pushing bytes into a socket nobody reads."*
6. Ana accepts, then presses **Stop** halfway through the next one: the bar goes, the half file is gone, and the
   sender stops. *"Nothing half-there is ever saved under its real name."*
7. Optional 20 seconds, only if the room is with you: Ana points `Save to…` at a folder that will not take the
   file → told in the bar, **and the offer comes back** → same file, second folder, lands. *"A typo is not a
   reason to make you send it again."*

### 6 · The hard cases, quickly (6:30) — narrated, one line each

Pick **two**, out loud, in the order they fit. Do not attempt all four.

* **Walk away** 📶: Client A stands and walks to the door mid-file. The transfer stops and says so; no half file
  in the folder, nothing left in `incoming\`. Then walk back and rejoin — *by hand*: **"Lantern never dials you
  back. A chat you are not in should look like a chat you are not in."**
* **Two people called Ben**: Client B joins with the nickname Ben. Both get in, and the room gets a line saying
  there are two of that name. *"We do not refuse the name, because you cannot change it from inside a room — we
  tell the room, because two lines from 'Ben' are two people."*
* **The room, deleted from under you**: Ana deletes `Retro` while Ben is typing in it. Ben is told, ends up in no
  room, Send goes grey, and the half-typed sentence is still in his box.
* **Radio off**: with a second laptop, press **Start a server** with Bluetooth off: `could not host: …`, naming
  the radio, window still there.

### 7 · Leave (7:30) — host types

1. Ana: File → **Quit** → the confirm box → Cancel first (*"it asks"*), then Quit.
2. Every client's window shows the box, and the bar is left saying **connection lost — …**. The windows step
   aside; nobody is left holding a Send button that can only fail.
3. Point at the bar. *"It does not say reconnecting, because there is no reconnecting. That is a decision, not a
   gap."*
4. Ana restarts Lantern: the rooms are exactly as they were left. *"The rooms outlive the chat. The chat does not
   outlive the laptop."*

### 8 — Close (8:00)

One line, then questions: **five modules, nothing in `UI/` refers to `Transport/`, which is why this is the same
program over localhost and over the radio — 413 tests, and the parts tests cannot prove are on a signed-off sheet
in `docs/testing/`.**

## If something goes wrong

| What happens | What to do, and what to say |
|:--|---|
| The scan list is empty | Say *"nothing is advertised until someone presses Start a server"* → check the host is hosting → if it still fails, say *"this machine is not paired any more; Windows forgot overnight"* and move to the fallback. Do not debug pairing in front of the class |
| A laptop has died / is not there | Run the whole eight minutes with `--loopback` on the surviving laptop, two windows. Same program, same screens, one radio fewer — and say exactly that, out loud, because it is a design fact about the layering rather than an excuse |
| The big file is too slow for the slot | Have a 200 KB file on the Desktop too (`notes.txt`) and demo the protocol with that; state that 12 MB crossed in *N* minutes during the field test, quoting the number off the m6 sheet |
| A button does nothing | Narrator reads what the **status bar** says — every answer this program gives is in the bar, so reading it *is* the answer. Then skip the step and keep the ending |
| Sound doesn't play | Say it once, then demonstrate F10 by pointing at the receiving window's bar and the file landing. Do not fiddle with the volume knob in front of the class; fix it before the next run |

Nobody in the demo says "it works on my machine". The fallback is planned, the fallback is the same program, and
the narrator knows both plans.

## Tagging v1.0

The tag is the build the sheet was walked on:

```powershell
git tag -a v1.0 -m "Lantern v1.0: Bluetooth group chat, rooms, pictures, files, sounds"
git push --follow-tags
```

`v1.0` means: the suite is green (413), the end-to-end sheet has been walked on at least two laptops, and every
scenario row in its sign-off table has a driver who is not the author of the code it drives. If a row is empty,
the tag is not ready — that table is the exit criterion, and it is in the repository so that "ready" means
something checkable after the presentation rather than before it.
