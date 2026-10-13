# The glyph is the advance

The message box showed a black outline 🙂 while the transcript, the emoji table and the toolbar above it all showed
the coloured one. Fixing that was a afternoon of measuring a control that turned out to be lying about three separate
things, and one of the things it was lying about had been quietly broken since the week it was written.

## The invariant, because everything else follows from it

An emoji in the message box is **a character**. It is counted by `ValidationRules.Length`, deleted with one press of
Backspace, sent on the wire as itself, and compared against what arrived. The colour on screen is paint laid over
that character and can be scraped off without touching it.

That rule is why the obvious fix was refused. An RTF `\pict` group in an *editable* RichTextBox is not the U+FFFC
object replacement character that every account of this says it is: it is a **space**, `0x20`, one ASCII space in
`.Text`. Type `a`, insert a picture of a face, type `b`, and the box holds `a b`. The full account is in
`docs/design/formatting-across-the-link.md`. What matters here is that the refusal was right, and that being right
about the invariant did not mean the box had to stay outline — it only meant colour had to be paint.

## `OnPaint` does not happen

The first overlay was written the way everybody writes one: `override void OnPaint`, `base.OnPaint(e)` first, then
the pictures. It did nothing. A counter in the override, a forced repaint, and the paint message arrives at the
window procedure — **once, counted** — while the override is never invoked, zero times. WinForms lets the RichEdit
window do its own painting and does not call back.

So the overlay is drawn from `WndProc`, after `base.WndProc` has handled `WM_PAINT`, on a `Graphics` for the window.
After, not before: paint underneath the control's own glyphs is paint nobody sees.

And this is where the afternoon got interesting. The placeholder word — the grey "Message" in an empty box, added
with the rest of the input area — had been drawn in an `OnPaint` override since then. **It had never once been on
the screen.** The test for it asked `box.ShowsHint`, which is a fact about the text; it went on passing over a
feature nobody had ever seen, and would have gone on passing for the life of the program. The replacement asserts
`PaintedHint`, which is the same claim made about the paint instead of about the text, and it is the only reason the
box says "Message" now.

The general shape of the mistake: **a test that asks a property has tested the property, not the thing the property
describes.** `ShowsHint == true` and "nothing is drawn" is a state this program lived in for weeks without a sound.

## A window that will not be photographed, and one way that works

`DrawToBitmap` on a RichEdit returns a picture with almost none of its glyphs in it — 31 dark pixels for a line of
text, measured — which rules out "prove it by screenshot of the control" and is why the tests call
`FaceOverlay.Paint` into a bitmap of their own.

Screen capture does work: `CopyFromScreen` at the control's position on the display shows exactly what a person
sees, artwork and all. But not immediately. Three captures of a box that had just been asked to repaint, with nothing
moving:

| capture | the box's white paper reads |
| --- | --- |
| first after `Refresh()` | `D7D7D9` |
| second | `E7E7E8` |
| third | `ECECED` |

that is, mid-composition, converging on `FFFFFF`. Read too early and the whole client looks like a grey rectangle
with no text in it, which is a very convincing measurement of something that is not true. Pump the message queue,
wait a few hundred milliseconds, capture, and only then count pixels.

## The glyph is the advance. The picture is not its own square.

`GetPositionFromCharIndex` answers the question "where is this character" with where the character's **advance**
begins. For a face in Microsoft YaHei UI at 9 points that is a cell 12 pixels wide — and, measured properly, that
is also exactly how wide the glyph is:

| size | advance | ink across | ink down, from the top of the cell |
| --- | --- | --- | --- |
| 7pt | 9 | **9** | +2 .. +11 |
| 9pt | 12 | **12** | +2 .. +14 |
| 11pt | 15 | **15** | +3 .. +18 |
| 13pt | 17 | **17** | +3 .. +20 |

Square, on the advance, starting on the cell's own left edge, sitting a little below its top. Not an em, and nothing
overflowing into the next character.

**The first version of this section said otherwise, and was wrong for two whole commits.** It reported 15 pixels of
ink on a 12 pixel advance and 21 on 17 — an em, a third wider than the advance — and geometry was built to match:
`advance + advance / 3`, starting one pixel inside the cell. That is what the user then reported seeing: a coloured
face sitting up and left of where the character is, with four pixels of black outline standing out on its right.

What those numbers were actually measuring:

* **a full stop.** The measurement string was `🙂.` — the period was three pixels of ink at 9 points and four at 13,
  which is the whole "em", and its height dragged the vertical bound down to the baseline like a period does;
* **a caret.** An active RichTextBox has a blinking caret in it, and a caret is a column of ink one line tall that
  moves when the control is activated. The plain comparison box, left unfocused so it could not have one, then
  painted nothing at all into the capture and looked like a blank.

The string that measures a glyph is a face and a run of spaces, with the caret parked at the end of the spaces.

So the square is the advance, and the offset below the top of the cell is four sixths of it rounded down — two
pixels at advances of nine and twelve, three at fifteen and seventeen. Derived from the advance rather than read off
the box's font, because a face typed at Large sits in a taller line than the message box's own font and has to come
down with it.

Then there is the other square, which is not the same thing at all: **a bitmap of a face is not a face that fills a
bitmap.** `ColorEmoji` rasterises the font's colour glyph and trims nothing, so the artwork arrives with air around
it, and the amount of air is not constant:

| bitmap side | 9 | 12 | 15 | 18 | 20 | 22 |
| --- | --- | --- | --- | --- | --- | --- |
| ink inside it | 6 | 9 | **12** | 15 | 17 | 18 |
| air left round it | 3 | 3 | **3** | 3 | 3 | 4 |

The air is **three pixels** at nearly every size from 9 to 25 — not constant, but constant enough to build on, which
is why the bitmap is asked for at the square plus three: a face that has to cover a 12 pixel square is rasterised 15
across, inks 12, and is drawn at a scale of exactly one.

Drawing such a bitmap into the glyph's square with `DrawImage(rect)` puts the face inside the square and low in it,
a couple of pixels off the character, at whatever scale the square implies. So `CachedGlyph` reports the ink it
measured when it rasterised, and `DrawOf` hangs the drawing from the **middle of that ink**, which is the one point
that may be placed by the square:

```csharp
var scale = side / (float)ink.Width;                    // bitmap asked for 3 wider than the square, so ~1.0
var x = glyph.X + glyph.Width / 2f - (ink.X + ink.Width / 2f) * scale;
```

The bitmap is asked for at the square plus three, so that its ink comes back at about the square and the scale lands
near one: rasterised at the size it is shown rather than stretched into it. A stretched face is a blurry one.

**The erase stays an ellipse, and it is still not decoration.** The antialiasing feathers about a pixel past the ink
at every edge, so the patch is the square inflated by one, and what has to go under the artwork is a face's
footprint, which is round. An ellipse inscribed in it is the shape of a face; a *rectangle* — which is what the
arithmetic invites — would fill in the corners where the tops of the neighbouring letters' strokes live. The corners
stay untouched, the round middle gets covered, and nothing visible is lost: everything the ellipse lies on is
somewhere the control's own outline glyph was already drawn.

Then the same window twice over, once with an ordinary `RichTextBox` and once with the message box, each measured in
a window of its own with the other one closed — the ink the control draws, and the ink of what we paint over it:

| size | the outline glyph's ink | the painted face's ink | ink outside the square |
| --- | --- | --- | --- |
| 7pt | x +0..+9 y +2..+11 | x +0..+9 y +2..+11 | 0 in both |
| 9pt | x +0..+12 y +2..+14 | x +0..+12 y +2..+14 | 0 in both |
| 11pt | x +0..+15 y +3..+18 | x +0..+15 y +3..+18 | 0 in both |
| 13pt | x +0..+17 y +3..+20 | x +0..+17 y +3..+20 | 0 in both |

Identical bounds, and nothing of the outline left standing outside them. That is the whole claim of the task, in
pixels: the colour face is where the character is.

## The line under it is not the line you measured

Every number above was taken from a box holding one face and nothing else, and that turned out to be the mistake: the
offset from the top of the cell to the bottom of the ink is not a property of the face.

RichEdit lays a line out on **one baseline**, and `GetPositionFromCharIndex` answers with the top of the *line* for
every character on it whatever size that character is. Pick Large, write a word, go back to ordinary size and write a
face: the line grows, the baseline moves down with the tallest ink on it, and the face's cell - all the control will
tell you - stays where it was. Paint the face at its own two-pixel offset and it rides near the top of the line, above
the words beside it, with a crescent of the black outline it was put there to cover still showing underneath.

Measured the same way as everything else here, with the outline glyph showing and nothing of ours painted on it:

| the line | face ink, below the top of the line | face ink, tall |
| --- | --- | --- |
| one ordinary face | 14 | 12 |
| an ordinary face beside Large letters | **20** | 12 |
| a large face beside ordinary letters | 20 | 17 |

So the question was never "how far down does a face's ink go" but "**where does the ink on this line end**", and that
is the bottom of the tallest ink on the line: `em + (em + 4) / 6` for an em measured as above - 14 at an em of 12, 20 at
an em of 17. A face is hung from that line instead, `the line's bottom - its own em` below the top of the line, which is
what puts a small face and a large word on the same floor.

### Getting the tallest ink at a price you can pay on a keystroke

Sizes can only be read through a selection, and there are two traps in reading them.

**A line containing a face never reports one font.** `SelectionFont` gives `null` whenever the highlighted runs differ
in *any* respect, and it counts the **family** as a respect. A line with a face in it is therefore reported as mixed
even when the whole of it was typed at one size, because the control borrows `Segoe UI Symbol` or `Calibri` to draw the
face and truthfully reports what it drew with rather than what it was told. So "assume the line is uniform unless it
says otherwise" is not available: it says otherwise on exactly the lines this is needed for.

**Reading is cheap per question and ruinous per character.** Measured here: the font of a highlight costs 0.03-0.09 ms
to read, and a highlight that *crosses a face* an order of magnitude more, because the control lays the fallback font
out again. Walking a line a character at a time cost **15 ms for forty characters** - 15 ms off every keystroke, since
every edit throws the previous answer away.

What works:

* **Faces are measured, not asked about.** Their ink is their advance, and the advance ends where the next character
  starts: two position questions per face, no selection touched, and no reliance on a font name that was never the
  box's own.
* **The words between them are asked in stretches.** Highlight the stretch: an ordinary sentence answers for all of
  itself in one question, and only a stretch that answers "mixed" is halved and asked again - cut on text-element
  boundaries, so a family emoji is never cut in half. Measured: seventy words, 2 questions; a face among sixty-three
  characters, 2 (walking cost 12); a line of faces alone, none.
* **Nothing the box can be told to write is bigger than Large**, so a line that has produced a Large has produced the
  answer, whatever is left unread on it.

## What is behind a face

The patch has to be the colour that was behind the glyph, or a selected face turns into a hole punched in the
selection. Both halves of that are sampled rather than assumed:

* a RichEdit selection is `SystemColors.Highlight` **exactly** — `FF0078D7` on this machine, pixel for pixel;
* when the box loses the caret it draws **no selection at all**: the pixel where the blue was reads `FFFFFF` again.

So an unfocused selected face is patched with the paper. Painting blue there would be inventing a selection nobody
is looking at any more, which is why the control asks for a whole repaint when it gains and when it loses the focus
— alongside the four movements that obviously need one: text, caret, size, scroll. A face is paint at a position,
and a position is only true until something moves.

## What did not change, and what to look at if another machine disagrees

The text was never touched, so there is nothing new to count, to truncate, or to explain on the wire. The tests say
so in the crude way: paint a sentence with a face in it and compare `Text` before and after, same string, same
length. A machine with no emoji font gets no bitmap from `ColorEmoji`, and `FaceOverlay` then paints **nothing at
all** — no patch either — so the box keeps the outline the control draws itself, which is the same fallback the
toolbar and the transcript use.

If it looks wrong somewhere else, the numbers to re-measure are in `CellOf`: that the ink is the advance, that it
begins on the cell's left edge, and how far below the cell's top it sits. They are *this* font at *these* sizes, and
`TheSquareCoversTheGlyphTheControlActuallyDraws` holds the square to those four measurements — it fails by four
pixels on the em-sized patch this replaced.

And measure it with the four traps above closed, because a pixel measurement of a control is very easy to get wrong
and very hard to suspect afterwards. The traps, each of which produced a confident and false number on this one
afternoon: punctuation counted as part of the glyph; a caret counted as ink; a window that has never been the
foreground one, whose capture is a blank; and a window sitting partly below the bottom of the screen, whose capture
is black, which reads as "no ink anywhere" the moment the paper sample is taken from inside it.
