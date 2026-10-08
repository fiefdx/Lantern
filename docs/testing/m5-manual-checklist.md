# M5 manual check — pictures, a file across the room, and a room that makes a noise

Evidence for the M5 exit criterion: *"scenario 4 with pictures + scenario 5 fully work; sound plays on
receive."* Covers **F7** (text and pictures, inline thumbnails), **F8** (a file to the room: folder chosen by
the receiver, progress, large files in chunks) and **F10** (a sound on arrival).

The automated suite covers all of it at window, wire and disk level (`ShareButtonUiTests`,
`PictureMessageTests`, `PictureTranscriptUiTests`, `FileTransferTests`, `IncomingFileUiTests`,
`SoundNotifierTests`). What this checklist adds is the four things a test cannot do: **look at a photo** and
say whether it is legible, **hear** the two sounds and tell them apart, use a **real folder** the person
actually wanted the file in, and run a real file over a **real radio** where the bandwidth is not a
loopback socket.

- **Machines:** A (host) ______________  B (client) ______________
- **Build (commit):** ______________  **Date:** ____________
- **Testers:** ____________________  **Radio used:** ☐ loopback ☐ Bluetooth

> Steps 1–9 can be done with two windows on one machine, started with `--loopback`. Steps 10–12 are the same
> steps over Bluetooth between two laptops, and they are the ones the milestone is actually about: a dozen
> megabytes over a loopback socket is a memory test, and over the radio it is a quarter of an hour of
> watching a bar.

## Before starting

1. `dotnet build Lantern.sln -c Debug` succeeds.
2. Have ready: one photo **over 2000 pixels** on a side (a phone photo is fine), one photo of a few hundred
   kilobytes, one file of **10–20 MB** (a video clip, a zip of photos), and one **empty folder you can point
   at** plus one folder you will not mind files landing in.
3. Note where Lantern keeps what it has received: `%LOCALAPPDATA%\Lantern\incoming\`. Open that folder in
   Explorer now. Anything in it at the end of this checklist that is still called `*.part` is a transfer that
   did not finish, and step 8 is where that is explained.
4. Both windows joined to the same room before starting. Note the time you start step 6 — the large file is
   the slow one, and knowing how long it took is the point of writing this down.

## F7 — pictures

| # | Do this | Expect | ✓ | Notes |
|---|---|---|---|---|
| 1 | In A: **Share** → pick the small photo. | The photo appears in A's own transcript straight away, as a thumbnail about 160 px wide, on its own line under `A: [image] filename.jpg`. B gets the same line and the same thumbnail within a second or two. | | |
| 2 | Look at B's thumbnail. | It is legible enough to tell what the photo is of. It is **not** full size — the point of a thumbnail is that a room full of photos does not become a wall of scrolling. | | |
| 3 | Double-click the thumbnail in B. | The picture itself opens in the system's viewer, at full size. A thumbnail nobody can enlarge is half a feature. | | |
| 4 | Share the very large photo (over 2000 px, or over the size limit the app states). | Either it arrives as a thumbnail like any other, or it is refused with a line in the status bar naming the file and saying why. It must not sit there failing silently, and it must not freeze either window. | | |
| 5 | In A: **Share** → pick something that is not a picture (a .txt, a .zip). | It goes as a **file**, not a picture: a `[file] name (N bytes)` line in both transcripts, and the receiving side gets the offer strip from step 6 below. One button, and the extension decides which of the two it is. | | |
| 6 | **Share** → cancel out of the picker. | Nothing at all. No message, no error, no empty transcript line. | | |

## F8 — a file across the room

| # | Do this | Expect | ✓ | Notes |
|---|---|---|---|---|
| 7 | In A: **Share** → the 10–20 MB file. | A's transcript gets the `[file]` line immediately, and the status bar shows a bar with a percentage that moves. **A stays usable** — type a message in the middle of the transfer and send it: it goes, and both windows see it, because the file does not own the connection. | | |
| 8 | Look at B while the file is arriving. | B's window shows a strip naming **who** is sending, **what** it is called and **how big** it is, with `Save to…` and `Decline`. The strip does **not** block the window: B can still read the room and type. The status bar shows its own percentage for the same file. | | |
| 9 | In B: click `Save to…` → pick the empty folder → let the transfer finish. | Nothing appears in that folder until the last chunk has arrived — then the file is there, whole, with its original name, and the status bar says where it put it. The bar goes away when the transfer is over rather than sitting at 100%. | | |
| 10 | Open the received file on B. | It opens, and it is the same file: same size in bytes as the one on A (right-click → Properties on both). This is the one check that says nothing was lost or duplicated in the chunking. **Size on A:** ____________ **on B:** ____________ | | |
| 11 | Send the same file again, and this time click `Decline` on B. | The strip comes down, the bar goes, B is told it was turned down, and A is told the transfer stopped — A's bar does **not** carry on to 100%. Nothing is written to B's disk, and no `*.part` is left in `%LOCALAPPDATA%\Lantern\incoming\`. | | |
| 12 | Send a file, click `Save to…` and choose a folder that will not have it (a read-only share, or a folder that already contains a **folder** of that name). | B is told in one plain line what went wrong, and **the offer comes back** so a different folder can be chosen. Choosing a sensible folder second time then saves the same file without A sending anything again. A file lost because somebody typed the wrong folder is the failure this step exists to catch. | | |
| 13 | Send a file, and while it is arriving close B's window (or shut B's laptop lid). | A's transfer stops within about twenty seconds and says so, rather than pushing bytes into a connection nobody is reading. A's bar does not hang at 60% forever. | | |
| 14 | On A, send a file to a room where B is **not** present (B in another room). | Nobody's window offers anything, and A is told the file went nowhere (or to nobody) rather than appearing to succeed. | | |

## F10 — the room makes a noise

| # | Do this | Expect | ✓ | Notes |
|---|---|---|---|---|
| 15 | Put both windows behind another program (Notepad in front, say). Have B send a message to A. | A plays the short message sound. It is short — under a second — and quiet enough not to startle, but audible over ordinary room noise at a normal laptop volume. | | |
| 16 | Send a picture the same way, from behind. | The same message sound. One noise for "something arrived in the room". | | |
| 17 | Send the 10–20 MB file, still with the windows behind, and wait for it to finish on B. | A **different** sound — the two-note one — and only once, at the end. Not one at the announcement and another at the arrival, and not one per chunk. | | |
| 18 | Send a file, then Decline it (or stop one in flight with the **Stop** button). | No file sound at all. A cheerful noise for a transfer that failed is how a feature like this gets switched off. | | |
| 19 | Send a message from A to… nobody (A is in a room on its own with no one else in it). | No sound on A's own machine: nothing echoes a person's own messages back at them. | | |

## Over the radio (the milestone's actual claim)

| # | Do this | Expect | ✓ | Notes |
|---|---|---|---|---|
| 20 | Two laptops, paired, no `--loopback`: send the 10–20 MB file from host to client. | It arrives, whole, in the folder chosen, with the sizes matching as in step 10. Write the elapsed time down: it is the number the report needs. **File size:** ________ **time:** ________ | | |
| 21 | While that transfer is running, on the receiving laptop: type a message and send it. | The message arrives in a few seconds, while the file is still going. The room keeps talking around a file — a file that owns the connection makes the chat useless for its duration. | | |
| 22 | Start a second file from the same sender while the first is still going. | Both move, both finish, and each arrives whole; the bar says there is more than one (`(+1)`). Two files crossing in opposite directions at once is worth a try too, if there are three laptops. | | |
| 23 | Walk one laptop out of range during a large transfer, then come back. | The transfer stops and says so; neither window crashes, and the half-file is *not* left in the chosen folder. (`*.part` files in the incoming folder are acceptable; a truncated file with the real name on it is not.) | | |

## Things to watch for, whichever way they come

- A bar that stops moving but does not stop: within twenty seconds it should say the transfer was stopped,
  not sit there.
- Any `*.part` left in the incoming folder after a **declined** transfer, or after a save that was reported as
  successful.
- A sound at a moment it shouldn't play, or the same sound twice for one event (step 17).
- A thumbnail that is actually a full-size photo squeezed into the transcript — the transcript should not grow
  a two-foot-tall line.
- Anything in the transcript that reads differently in A's window and B's window for the same event. Every
  `[file]` and `[image]` line should be identical in both, including its timestamp, because the two people
  need to be able to agree afterwards what was sent.
- A window that stops responding for more than a moment or two at any point. Nothing in M5 should ever block
  the message loop: the file work is off in the background, and the progress bar is *read* four times a second
  rather than driven by the bytes.

**Observations, in the people's own words:**

_______________________________________________________________________________

_______________________________________________________________________________

**Verdict:** ☐ M5 exit criteria met ☐ not met, because ______________________________________
