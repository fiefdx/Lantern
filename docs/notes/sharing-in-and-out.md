# Sharing in both directions

v1.2. Two things people asked for once they had sent a picture and a file between two laptops: *how do I get this
onto my own machine*, and *is it still going*.

## The way back out

A received **file** already asked where it should go — the offer strip, `Save to…`, a folder rather than a file
name. What it did not have was a **second chance**. The strip comes down the moment it is answered, and the
transcript goes on showing `[file] notes.txt` for the rest of the afternoon. A photo was worse: it arrives inline,
nobody asks anything about it, and the only way to get it onto a disk was to double-click it, find a toolbar, and
press a button in a window that had opened to show it to you.

So the way back is the transcript itself, which is where the thing actually lives:

> right-click an entry → **Save picture to…** / **Save file to…** → choose a folder

Four decisions in that, and each one is a thing that could have gone the other way.

**A folder, not a file name.** The same seam the offer strip has always used (`AskForFolder`), so there is one
answer in the program to "where do received bytes go", not two. A receiver choosing a folder is saying *where*,
not *what* — which is also the only thing that works when three files arrive in the same minute.

**A name that is taken gets a number, not a replacement.** `sunset.png` already in Documents means the second save
writes `sunset (2).png`. A chat window that quietly overwrote a file the person never mentioned would be believed
when it said "saved", and the file it destroyed would not come back.

**A file this window sent is not offered back.** The bytes are already on this machine. A menu item that saves a
file onto itself is a button that does nothing, and the only way to find that out is to click it.

**An ordinary sentence shows no menu at all.** Right-clicking words could have shown one greyed item; that is the
window claiming it does not know what was right-clicked when it knows exactly. Nothing is the honest answer.

For a file, the writing goes through `SaveReceivedAsync` — the offer strip's own path — so the spool's rules, its
collision handling, and its reasons for refusing all apply here too rather than being re-invented. Saving a **photo**
writes `PictureMessage.Data`: what was sent, not what the thumbnail was shrunk to, because the point of saving a
photo is the photo.

Asking twice for the same file is answered with where it already is (`notes.txt is already saved at D:\…`) rather
than with a second copy and silence. That needed a small memory — `savedTo`, by transfer id — and it is the kind
of thing only a test finds: `Assert.Single(service.SaveRequests)`.

## What a bar is asked

A file's progress line used to read `notes.txt 34%`. A bar is asked two questions, and that answers neither:
**which way** is it going, and **how much** of it. It is now:

```
receiving notes.txt 34% - 1.7 MB of 5.0 MB
```

The direction comes from the one fact already in the job — whether its sender is this window's nickname — so a
relayed file cannot be mislabelled, and "saving" (the bytes are in and being written out) keeps the word it had.

**A picture has no percentage, and says so.** Its bytes are a message, and a message is one frame; there is no
counting the middle of a frame without changing the wire. A fifth of a megabyte a second over RFCOMM means a five
megabyte photo was a minute and a half of motionless window with nothing but a greyed Share button — which is
exactly what a crashed program looks like to somebody who has not seen one send a photo before.

What can honestly be said is that it is going, how big it is, and how long it has been going:

```
sending sunset.png - 3.2 MB - 12 s
```

with a marquee bar. **No Stop button**, deliberately: half a frame is not a state the wire can stop in, and a
button that cannot stop anything is worse than no button. If a file is moving at the same time, the file takes the
bar — a real percentage beats a moving stripe — and the photo's line comes back when the bar is free again.

That is the whole honest menu for a single frame. The alternative was to make the number up.

## No test had ever opened the menu

Reported after the commit: *right-click an image, no menu*. It could not be reproduced in a live window — a real
`WM_CONTEXTMENU` sent to the transcript raised the menu over a received photo, over a photo this window sent,
before and after a room switch — and yet the suite had eight tests over this feature and not one of them had ever
shown the menu.

That is the same hole v1.1.1 reported, in different clothes. `CanSaveSharedAt(caret)` answers the *rule*, and says
nothing about whether a menu is attached to the control, wired to the handler, or cancelled on its way out.
`DesignViewTests` builds every window and would not notice a transcript with no menu at all. Four tests now send
the message a mouse sends and assert on the popup: it appears over a photo and says *Save picture to…*, it appears
over a file that arrived and says *Save file to…*, it does not appear over a sentence, and a caret on the line
break that ends an entry counts as that entry.

Which is also why the hit test changed. A click a hair past the end of a caption line, or just under a thumbnail,
lands on the line break that ends the entry — and a menu that refuses across a two pixel gap is read as an
inability, not as precision. So the menu now takes any caret on a line that begins inside the entry, and still
nothing below the last line. The double click keeps its stricter rule on purpose: a photo opened that nobody
clicked is worse than nothing, while a menu that agrees a line belongs to its entry is not guessing.

## Two things the harness got wrong

**A test that only `await`s does not pump the window's queue.** A save finishes on a worker thread and posts its
line back to the window; in a test there is no message loop to dispatch it, so the posted line sits in the queue
unread. The assertion I wrote — *the bar says "already saved at"* — failed against a program that was saying it,
and the file count and the service's own log both agreed the save had happened. `Application.DoEvents()` in a
short loop before reading what an operation *said*; the running program has a message loop, the harness does not.

The trap is that this makes a status-bar assertion depend on something the test has to remember. Anything that
reads text written by an `OnUi` continuation needs the pump; anything that reads a file, or a fake's log, does not.

**A fixture sized by hope.** `APhotoBiggerThanTheWindowIsShownWholeAndScrollable` made a 4000 × 3000 PNG — 48 MB
of GDI+ — on the theory that no window is that wide. Alone, it passed. In a run behind five hundred windows, GDI+
refused it, and it became the suite's first order-dependent failure. "Bigger than the window" is a property of the
window, so the window is now 600 pixels wide and the photo a modest 1400. Nothing was lost from the claim, and a
test that fails only in company gets deleted as flaky.
