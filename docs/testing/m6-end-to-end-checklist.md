# Lantern v1.0 — end-to-end checklist (the six scenarios)

Milestone M6 · task T6.3 · area QA · **run against the tag you are about to call v1.0, not against a working tree**

This is the sheet the class was asked for: every use-case scenario from the proposal, walked through end to end
on two laptops, with a person who did not write the code driving it. The automated suite (413 tests) proves the
logic; this sheet proves the program on the hardware it is for.

## How to use this sheet

* **Two laptops minimum.** A third is better: scenarios 3, 4 and 5 have a "two clients at once" step that a
  laptop and a host cannot show by themselves.
* Each scenario is run by **two people**: one drives, one records. Neither may be the owner of the module under
  test (`CONTRIBUTING.md`: *"QA — rotates — never the author of the code under test"*). The owner may sit there
  and say nothing.
* A step is **green only when the expected line is what actually appeared**, quoted in the notes column if it is
  text. "It worked" is not a result.
* Anything amber or red: write down the build (`git describe`), both machine names, and what the status bar said.
  Then file it as an issue and keep going — a red step does not stop the sheet, it stops the tag.
* Bluetooth steps are marked 📶. Loopback steps (marked 🔁) can be done on one laptop with
  `Lantern.exe --loopback` twice, and are how the sheet is rehearsed before the lab is booked.

**Preflight, once, before any scenario:**

| # | Check | Green how |
|---:|---|---|
| P1 | Both laptops on the same build | `git describe --tags` matches on both, and both are the tagged commit |
| P2 | Radio on and paired | 📶 Windows says "Kitchen Table is ready" (or the machine's own name); `docs/testing/bluetooth-pairing-how-to.md` if not |
| P3 | Nothing else holding the service | no other Lantern is hosting on either machine, and no leftover `Lantern.exe` in the tray |
| P4 | Sound audible | Windows volume up, and the sound scheme not set to "No Sounds" |
| P5 | Rooms file where it should be | `%LOCALAPPDATA%\Lantern\rooms.json` exists on the host after a first run |
| P6 | Empty incoming folder | `%LOCALAPPDATA%\Lantern\incoming\` has no `.part` files left in it |

---

## Scenario 1 — Host a chat

*Owner of the code under test: UI (Haitao). Driver must not be Haitao.*

| # | Do this | Expected | ✓ |
|---:|---|---|---|
| 1.1 | Start Lantern on the host laptop | The startup window. The nickname box already has a suggestion in it; the status bar says which transport this is ("Bluetooth …"), before anything is clicked | ☐ |
| 1.2 | Type a nickname, click **Start a server** | The host window opens titled **Lantern - host** and the status bar says **server online** | ☐ |
| 1.3 | Look at the room list | The rooms from the last run are there — the ones in `rooms.json`, not three invented ones | ☐ |
| 1.4 | Quit and start again | The same rooms come back. Then delete one, quit, start again: the deleted one stays deleted | ☐ |
| 1.5 | 📶 With the Bluetooth radio switched **off**, click Start a server | `could not host: …`, in a sentence that names the radio, and the window is still there with both buttons working. No crash, no dialog to dismiss | ☐ |
| 1.6 | Start a second Lantern on the same laptop and try to host | It says it could not host, rather than opening a second host that steals the first one's clients | ☐ |

## Scenario 2 — Join a chat

*Code under test: Transport (Victory) and UI (Haitao). Driver must be neither.*

| # | Do this | Expected | ✓ |
|---:|---|---|---|
| 2.1 | On the second laptop, click **Scan** | The button changes to say scanning; the host's machine appears in the list under the name Windows gives it | ☐ |
| 2.2 | Scan again with nothing hosting | An empty list and a sentence saying so — never a machine invented to fill the box | ☐ |
| 2.3 | Select the host, click **Join** | A client window opens titled **Lantern - client**, status bar **connected**, and the host's room list is in it | ☐ |
| 2.4 | Look at the host's window | The new person is in the member list, and a line `Bob - join this chat` is in the room they joined | ☐ |
| 2.5 | Join a machine that is not there (🔁: shut the host down between selecting and joining) | A sentence about the machine that could not be reached, in a human voice, and the startup window still usable | ☐ |
| 2.6 | Cancel a scan halfway | What already answered stays in the list; the button is a scan button again | ☐ |

## Scenario 3 — Manage rooms

*Code under test: Storage (Julia) and Core (Okechukwu). Driver must be neither.*

| # | Do this | Expected | ✓ |
|---:|---|---|---|
| 3.1 | On the host, create a room | It appears in the host's list, and in every connected client's list without anyone pressing anything | ☐ |
| 3.2 | Check *rooms.json* **without restarting** | The new room is in the file already. Not on quit — right away | ☐ |
| 3.3 | Rename a room that has people in it | Everyone still inside it; the name changes everywhere, and the member list is unchanged | ☐ |
| 3.4 | Delete a room that has people in them | The people inside are told, end up in no room, and their Send button is grey rather than waiting to fail | ☐ |
| 3.5 | On a **client**, look in the Edit menu, and try the New/Delete buttons | They are greyed or they say "only the host can rename rooms". A client cannot edit rooms by any route, including the menu | ☐ |
| 3.6 | Duplicate a room name, and try the two names that are the same but for case | Refused, with the reason in the status bar | ☐ |
| 3.7 | Type the half of a message in a room, then delete that room from the host | Nothing thrown at the client, and the half-typed sentence still sitting in their box | ☐ |

## Scenario 4 — Chat

*Code under test: Core (Okechukwu), UI (Haitao), Transport (Victory). Driver must be none of them.*

| # | Do this | Expected | ✓ |
|---:|---|---|---|
| 4.1 | Two people in the same room exchange two text messages | Both windows show both lines in the same order, attributed to the right person; neither sees their own words twice | ☐ |
| 4.2 | Open a second room and send something in each | Nobody in Room 1 sees Room 2's conversation, in either window | ☐ |
| 4.3 | Send a picture (Share → a photo) | A thumbnail appears in the room, in every window; clicking it opens the real file | ☐ |
| 4.4 | Watch while the picture arrives | One short sound per incoming message. A file announcement is not a second sound | ☐ |
| 4.5 | Have a third person enter and leave | `Cara - join this chat` and `Cara - left this chat` appear in that room only, in every window | ☐ |
| 4.6 | Send a message too long for the protocol | Refused where it is typed, with the reason, and the text stays in the box so it can be shortened | ☐ |
| 4.7 | Send while disconnected (kill the host, then press Send) | A sentence, promptly — no hang, and no window ending because a socket stopped | ☐ |
| 4.8 | Two people who both chose the name Alex join the same room | Both get in, and the room gets a line saying there are two of that name; nobody is stranded outside for a name they cannot change | ☐ |

## Scenario 5 — Share a file

*Code under test: Transport (Victory) and UI (Haitao). Driver must be neither.*

| # | Do this | Expected | ✓ |
|---:|---|---|---|
| 5.1 | Bob sends a small file (a text file, say) | Every other member's window shows an offer strip naming the file, its size and who sent it — and Bob's own transcript has the file line immediately | ☐ |
| 5.2 | One receiver accepts and chooses a **folder**; another ignores it | The one who accepted gets the file, named what it was named, in the folder they pointed at. The one who ignored it keeps being offered it, and the sender is not stopped | ☐ |
| 5.3 | Watch the progress bar to the end on both directions at once (Bob sends while Ana sends) | One bar, showing whichever file was last worth showing; both files arrive | ☐ |
| 5.4 | Decline a file | The strip goes, nothing is written, and the sender is told the machine sending it is not wanted any more — its bar stops rather than pushing bytes into a socket nobody reads | ☐ |
| 5.5 | Stop a file halfway (Stop button) | The bar goes, the receiver's half-file is deleted, and **the sender's transfer stops too** | ☐ |
| 5.6 | Send a file of real size — a dozen megabytes or more 📶 | It crosses. Write the elapsed time and the byte count on **both** machines in the notes; both spools are gone afterwards | ☐ |
| 5.7 | Choose a folder that will not take the file (a read-only share, or a folder deleted a second ago) | Told in the bar, **and the offer comes back** so a different folder can be chosen. The sender does not have to send it again | ☐ |
| 5.8 | Close the folder box without choosing | Not a refusal: the offer stays up, the bytes keep arriving | ☐ |
| 5.9 | Have a file arrive while a strip for another file is up | It queues ("1 more waiting") and is still there to accept after the first is answered — including one that finished arriving while nobody was answering | ☐ |
| 5.10 | 📶 Walk the receiver out of range mid-transfer | The transfer stops and says so. No half file in the chosen folder, and no `.part` left in `incoming\` | ☐ |
| 5.11 | Check the size on disk | Byte-for-byte the same size as the original, on every receiver who accepted | ☐ |

## Scenario 6 — Leave

*Code under test: Core (Okechukwu) and UI (Haitao). Driver must be neither.*

| # | Do this | Expected | ✓ |
|---:|---|---|---|
| 6.1 | On the host, File → Quit | Asked to confirm first. Cancel leaves the server running | ☐ |
| 6.2 | Confirm | The host window closes, the server is really gone (a scan no longer lists it), and the start screen is back within a second | ☐ |
| 6.3 | Look at a client's window | A box saying the connection was lost, and the status bar left saying **connection lost — …** after the box is acknowledged. The client window steps aside rather than offering a Send button that can only fail | ☐ |
| 6.4 | Read the client's bar closely | It makes **no promise of reconnecting** — Lantern does not dial anybody back on its own | ☐ |
| 6.5 | Pull the radio out / sleep the host laptop instead of quitting 📶 | The same report, and it arrives in a bounded time rather than whenever the socket notices | ☐ |
| 6.6 | A single client leaves (Leave button) while the server runs | The room is told, the member lists follow, and everyone else carries on talking | ☐ |
| 6.7 | Both laptops after the chat | Each is back at its own start screen and can host or join again **without restarting the program**; the last room change made before quitting is in `rooms.json` either way | ☐ |
| 6.7 | Host restarts | The rooms are there, exactly as they were left. Clients have to be joined again by hand — nothing dials itself | ☐ |

---

## Every feature, and where it is proven green

| Feature | Milestone | Steps on this sheet |
|:--|:--|:--|
| F1 WinForms app (.NET 10) | M0 | 1.1, 1.2, 2.3, every step in which a window is looked at |
| F2 Bluetooth networking | M4 | P2, 1.5, 2.1, 5.6, 5.10, 6.5 |
| F3 Server discovery | M4 | 2.1, 2.2, 2.6, 6.2 |
| F4 Nickname | M0 / M2 | 1.1, 2.3, 4.8 |
| F5 Room management (host) | M3 | 3.1 – 3.7 |
| F6 Room list & switching | M2 / M3 | 2.3, 3.1, 4.2, 6.7 |
| F7 Text & picture chat | M2 / M5 | 4.1 – 4.7 |
| F8 File transfer | M5 | 5.1 – 5.11 |
| F9 Room persistence (rooms.json) | M3 | 1.3, 1.4, 3.2, 6.7 |
| F10 Sound notification | M5 | 4.4, P4, and 5.1 for the file sound |
| F11 Join / leave notices | M2 | 2.4, 4.5, 6.6 |
| F12 Room member list | M2 | 2.4, 3.3, 4.5, 4.8, 6.6 |

Nothing in that table is covered only by an automated test: every feature has a step here that a person watches
happen on hardware.

## Sign-off (the contract rule, on paper)

| Scenario | Drove it | Recorded it | Author of the module under test | Date | Result |
|---|---|---|---|---|---|
| 1 — Host a chat | | | Haitao (UI) | | |
| 2 — Join a chat | | | Victory (Transport), Haitao (UI) | | |
| 3 — Manage rooms | | | Julia (Storage), Okechukwu (Core) | | |
| 4 — Chat | | | Okechukwu (Core), Victory (Transport) | | |
| 5 — Share a file | | | Victory (Transport), Haitao (UI) | | |
| 6 — Leave | | | Okechukwu (Core), Haitao (UI) | | |

All six rows green, on at least two laptops, with a driver who is not the author of what they drove, is the exit
criterion for M6. `v1.0` is tagged after that table is full — not before.
