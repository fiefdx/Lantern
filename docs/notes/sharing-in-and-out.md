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

## A file has a face now

A photo looks like a photo and a file looked like a sentence. So a file's entry gained a small page (40 × 46) with
its own extension written on it, drawn by `FilePlaceholder`.

**Drawn, not shipped.** There is no icon file in the repository to forget to copy next to `Lantern.exe` — the way the
sounds had to be thought about — and `deck.pdf` and `deck.zip` are visibly two things, which one generic icon is
not. The cost of drawing it in code is that its colours are decisions, so they are named (`InkColor`, `RuleColor`)
and measured against the transcript's white like every other colour in the window.

**Right-click the page, get the file.** The page is written *inside* the entry's span, before the span is closed,
so the caret the menu asks about lands on the file. That is the whole of what makes the icon more than decoration:
`Save file to…` is now reachable from the thing that looks like the thing.

**And double-clicking it opens nothing.** `TryOpenPictureAt` looks only at `pictures`. A file's bytes may still be
arriving, or may have been declined, so a viewer on a file is an empty window — and empty windows are what this
note's neighbour is about.

## Two things a RichTextBox does that a test has to know

**Embedded pictures are not in `Text`.** A thumbnail or a page contributes nothing at all to the string the control
reports — the line it sits on reads as a single space. `Text.IndexOf('\u0001')` finds nothing, and a test written on
that assumption fails with "there is no picture in the transcript" against a transcript that has two. Count them in
`rtb.Rtf`, or find them by line: the picture is on the line *below* the entry's words.

**`rtb.Rtf` does not give back the bytes you put in.** The control re-encodes every picture when it is asked for its
RTF — different PNG, different hex. So the test that checks a file's entry shows an icon and not a rendering of
the file's contents cannot compare bytes; it decodes the PNG out of the control's own RTF and checks its **size**:
40 × 46 is a page `FilePlaceholder` drew, and 13 bytes of PDF is not a picture of anything.

That is also why a v1.1 assertion had to be re-expressed rather than deleted. `Assert.DoesNotContain("\pict", rtf)`
was guarding "a PDF must not sprout a thumbnail" — true only until a file was allowed to have an icon. What was worth
keeping was the distinction between *a rendering of what is inside* and *an icon*, and the old measurement could not
make it.

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

## The right click that did nothing

Reported against v1.3: *right click file placeholder not working, not shows save to menu*.

**The first suspect was measured and cleared.** Everybody's instinct - and mine - is that the hit test is a
character-counting house of cards, and that a click in the middle of an inline picture lands somewhere else
entirely. A scratch window was built to find out what the transcript actually answers, and the answer was that a
click in the middle of the 40 x 46 page finds the file, and a click in the middle of a thumbnail opens the viewer.
The rule is forgiving enough. It was not the bug.

What *was* wrong with the tests is still worth having: they aimed at the **character** the picture is, which is the
**top-left pixel** of an icon. Everything else about the thing on screen was untested. `ARightClickInTheMiddleOfThePageOffersToSaveThatFile`
and `ADoubleClickInTheMiddleOfAThumbnailOpensTheViewer` aim in pixels now, at the part a person actually points at.

**Three things could really do it, and all three are fixed.**

1. **The file was one this window sent.** v1.2 refused that deliberately - the bytes are already on this machine, so
   offering them back looked like a menu item that does nothing - and the refusal was invisible. A right click that
   produces nothing at all cannot be told apart from a broken program, and it was reported as one. There is now
   **Save a copy to...**, which is the thing a person pointing at their own file actually wants (to email it, to put
   it on a stick), and it works because the window remembers the path it read the file from - Lantern keeps no copy
   of what it streams. If that file has moved or been deleted, the bar says which file and where it was last.
2. **The menu was raised where the mouse was not.** The offer hung entirely on `Cursor.Position` at the moment the
   menu opened - a question the keyboard's menu key, a touch and hold, and some touchpads do not answer. The control
   has a caret, and a right click puts it where the click happened, so the caret is asked too
   (`AMenuRaisedWithoutTheMouseFollowsTheCaret`, with the cursor parked at -32000,-32000).
3. **The window had been redrawn.** `ClearTranscript` forgot the spans, which it must, and also which transfers this
   window has, which ones it sent, and where it already wrote one out. A transcript is redrawn every time a room is
   entered; those are facts about files, not about the text on screen.

**And what a refused right click now does is say so.** The bar reads *right-click the page, or the line, of a shared
file to save it*. The menu stays away from an ordinary sentence on purpose - a one-item menu with a greyed item is a
window claiming it does not know what was right-clicked - but silence about a gesture nobody can be expected to have
guessed is not a design, it is a bug report.

## The question v1.3.1 could not answer

A file's entry is drawn by `Append(new FileMessage(...))` in `ShareFile` and `OfferFile`. Text and pictures also
make their way into the room's **own** transcript, which is what `ShowTranscript` replays when a room is entered;
nothing does that for a file. With the fake, that shows up plainly: send a file, click into another room, click
back, and the page is not there.

Two tests were written for that - *a file is still saveable after looking at another room*, and the same for the
promise that a file already saved is not written twice - and **both failed for a reason one level down**: the entry
was not in the transcript at all, so there was nothing to be saveable. They were removed rather than left green.
Making the fake record file entries would have turned them green while saying nothing whatever about the real
engine, and a passing test the real program contradicts is worse than an open question.

So the clearing of `transfers` was still fixed - a window must not forget files it has, whether or not it can
currently be shown one - and the open question is on the checklist as step 19, where two machines can settle it in
ten seconds: *enter the room with a file in it, go away, come back. Is the page still there?* If it is not, that is
a v1.3.x bug of the same family as the empty photo viewer: the entry exists in the moment and nowhere else.

