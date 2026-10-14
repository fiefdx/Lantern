# The window in three columns, and each column owning what is in it

Asked for in one sentence:

> let's optimize the chat gui layout, it should be left, middle, right 3 columns, left is rooms list, right is
> members list, and middle is the chatting area, it split into two rows, top row is the transcript area, bottom
> row is the input row, in input row, input row split into two rows, top row of the input row is input editor tool
> bar, it include all functions, and the bottom row of the input row is input element and the send button.

That reads like a request for tidiness, and it was one — but not of the decorative kind. The window was already
*called* three columns. It had stopped being them.

---

## What the grid actually was

One `TableLayoutPanel`, four columns and four rows, everything a cell in it:

| | col 0 (190) | col 1 (%) | col 2 (88) | col 3 (170) |
|---|---|---|---|---|
| 0 (28) | Rooms header | *(nothing)* | *(nothing)* | Members header |
| 1 (%) | rooms list | transcript | *(nothing)* | members list |
| 2 (30) | **tool strip, spanning all four columns** | | | |
| 3 (54) | *(nothing)* | message box, spanning two | *(nothing)* | Send |

Three things fall out of that table, and only the first was ever noticed:

* **The tool strip lay across the whole window.** Its left half sat over the rooms list and its right half over the
  members list. Nobody plans that; it is what "span the window" means when the window is a grid. What a person sees
  is a row of buttons whose left end is attached to nothing — and a button with nothing under it is a button that
  looks disabled.
* **An empty column, 88 pixels wide, between the message box and Send**, because the box was told to span two
  columns and nobody could say which second column it was spanning.
* **Two rows of nothing above the transcript**, which is where the headers used to line up with each other across
  the whole window.

None of that is a crash. All of it is the window saying something other than what it means.

---

## What it is now

```
┌─────────────┬────────────────────────────────────────────┬────────────┐
│ Rooms    + −│ transcript                                 │ Members    │
│             │   [2026-03-07 14:05] User 2                │            │
│ room        │   the part somebody typed                  │ one        │
│ room        │   …                                        │ two        │
│ room        ├────────────────────────────────────────────┤ three      │
│             │ B I U  Aa  A                  Emoji Share  │            │
│             ├────────────────────────────────────────────┤            │
│             │ message                              Send  │            │
└─────────────┴────────────────────────────────────────────┴────────────┘
```

```
tableLayout                 190 │ 100% │ 170   — one row
├─ pnlRooms                     header over the list of rooms
├─ pnlChat                      1 column, 2 rows: transcript, then input
│  ├─ rtbChat                   takes whatever room is left
│  └─ pnlInput                  2 columns, 2 rows, and it is as tall as it needs to be
│     ├─ pnlFormat              the tools, across the top of both columns
│     ├─ txtMessage             the box
│     └─ btnSend                beside the box, on the box's row
└─ pnlMembers                   header over the list of members
```

**The rule the layout now holds: a pane belongs to exactly one column.** Not "the panes are roughly grouped" — the
tool strip is *inside* the middle column, so it cannot be over the rooms list even by accident, and the box and
Send are inside it too, so the tools are unambiguously the tools *of that box*. That is the difference between a
toolbar and a row of buttons that happen to be nearby, which is the thing the strip was built to remove and then
started doing again by itself.

Three things were deliberately not changed:

* **Each side column carries its own header** rather than the window having a header row. Visually the two headers
  still line up with each other; structurally they now belong to the column they head, and the middle column is free
  not to have one (the transcript has never had a caption).
* **Send stays on the box's row, not on the strip.** Everything that puts something *into* a message is on the
  strip — the five style tools and Emoji and Share. Send is what *ends* a message, it is the biggest muscle memory
  in the window, and Enter does the same thing. Moving it would be a change for symmetry's sake.
* **The transcript is the row that gives.** `pnlInput` is `AutoSize` over two fixed rows, and its row in `pnlChat`
  is `AutoSize`, so a window dragged taller hands the extra to the transcript rather than padding out the two rows
  of the input area. That is asserted, not assumed.

---

## Two things the ruler found

A layout is not a drawing; it is numbers in a box a person can resize, on a machine whose text may not be the size
these numbers were chosen on. Both of those produced something.

**At the window's minimum width, Emoji was sitting on top of Underline.** The strip is a row of fixed-width buttons
in a space that shrinks: the style tools at the left with fixed positions, Emoji and Share anchored to the right
edge. Narrow the window and the right-hand group walks left into the left-hand group. `btnShare` had been 170 wide
only because it used to fill a 170-wide column — it is the word "Share". So: Bold/Italic/Underline 30, Size and
Colour 40 (182 across, from 208), Share 70, Emoji 88. Measured at minimum size, the gap between the two groups is
now 10 pixels. Before it had been an overlap, and the test that caught it named the two buttons by name: Underline
and Emoji.

The failure mode is worth naming: not an ugly layout, but **clicking Underline and having the emoji table open**.

**On a machine with taller text, the bottom of every tool was shaved off.** These rows are absolute numbers, and
this machine's font metrics came out at a line height of 17 where the numbers were chosen at 15 — so a 24-tall
button became 27 inside a strip that had become 28, and the room action buttons hung 1px out of their header. Both
were invisible in the arithmetic and visible in the dump. The tool row is now 34 rather than 30 with the buttons at
y=2, and both headers are 30 rather than 28.

So the geometry assertions measure **on the screen**, in one coordinate system, and two of them exist only because
of this: `TheToolsDoNotFallOnOneAnotherWhenTheWindowIsAtItsNarrowest` (no tool outside the strip's ends, no two
tools intersecting, at `MinimumSize`) and `TheRoomButtonsStayInTheColumnTheyBelongTo`.

---

## What the tests say now

`WireframeParityTests` is the file that keeps the window honest against the figure, and it gained three tests:

* `TheWindowIsThreeColumnsWithTheChattingAreaInTheMiddle` — column indices *and* edges: rooms right of nothing, chat
  right of rooms, members right of chat; each list inside its own column under its own header.
* `TheChattingAreaIsTheTranscriptOverTheInputArea` — the two rows of the middle column, and a taller window giving
  its extra height to the transcript.
* `TheInputAreaIsItsToolbarOverItsBoxAndSend` — the strip across the top of the input area, the box and Send sharing
  the row under it, all seven tools on the strip, Send not, and none of it reaching outside the middle column.

One older assertion had to be turned the other way: `StyledComposerTests` used to insist that Send was **not** a
sibling of the box (it was a cell of the outer grid, and the strip was another). Now Send *is* a sibling of the box
and the two share a row — which is what "the bottom row of the input row is input element and the send button"
means — and what is asserted instead is that Send is beside the box rather than up on the strip.

Nothing in `Core`, `Transport` or `Domain` moved: this is the window, and the window had never been the part at
risk. **664 tests, no warnings.**
## And the three columns end on one line

Reported later by somebody looking at the window rather than at the code: **"make its bottom border the same level
as room list and member list bottom borders - currently the bottom border of input area has too much padding or
margin, making it a little higher than others."**

Three pixels. That is nothing to describe, which is why the report is written in words like "a little higher"
instead of numbers, and it is a real complaint: what is being lined up is the three bottom borders that a person
reads as the bottom edge of the window, and three panes stopping at three different heights makes a window look
unfinished without anybody being able to say why.

### Measured, on the screen

The first measurement reported a gap of **485 pixels**, which was the measurement being wrong rather than the
window: `Bottom` is in the parent's coordinates, and these four controls are not siblings - the list's parent is a
column panel, the box's parent is the input panel. Only the screen is a frame of reference they all share, so
everything about this bug is measured in `RectangleToScreen`, which is also why the assertion below asks the
*parent* to do the mapping. And the outer edge, not the client area: a list and a rich text box are drawn with a
two-pixel frame and a button with a one-pixel one, so a client-area comparison would have put Send a pixel off
something that was already right.

| Bottom border | before | after |
|:--|:--|:--|
| Rooms list | 689 | 689 |
| Message box | **686** | 689 |
| Send | 687 | 689 |
| Members list | 689 | 689 |

The middle column was spending two margins where the other two spend none. The lists are docked into plain panels,
and their bottom borders land on those panels' bottoms; the chatting area spent three pixels below `pnlInput` and
three again below the box. So `pnlInput`, `txtMessage` and `btnSend` carry no bottom margin now, and all four edges
are on one line - and the three pixels went into the message box, which is the only thing in the window that gained
anything by the change.

```
pnlInput.Margin   = new Padding(3, 3, 3, 0);   // was the default 3,3,3,3
txtMessage.Margin = new Padding(3, 3, 3, 0);   // was the default 3,3,3,3
btnSend.Margin    = new Padding(3, 3, 3, 0);
```

### Looked at as well as measured

The bottom of a real window was photographed and the frame lines found in the pixels: rooms list, message box and
members list all end at the same row, with Send one pixel below rather than three above. Which is the right way
round for this report - a person comparing two borders notices the one that is *not* where the others are.

`WireframeParityTests.TheThreeColumnsEndOnTheSameLine` is the assertion: the box's bottom border equals the rooms
list's and the members list's, Send's equals the box's, and the transcript still meets the input area with no gap
opened between them - because shortening the input area by three pixels is meant to hand those three to the
conversation, not to leave a stripe of empty panel under it.

**711 tests, no warnings.**
