# The glyph is wider than its advance

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

## The cell is an advance. The glyph is an em.

`GetPositionFromCharIndex` answers the question "where is this character" with where the character's **advance**
begins. For a face in Microsoft YaHei UI at 9 points that is a cell 12 pixels wide.

This is what the control actually drew in that cell — a lone face on a line, nothing of ours painted on it, ink
measured off the screen:

| size | cell | ink across | ink down |
| --- | --- | --- | --- |
| 9pt | 12 wide | **15** | 14 |
| 13pt | 17 wide | **21** | 21 |

The emoji this font supplies is drawn at an **em**, and the em here is a third wider than the advance again. It
starts a pixel inside the cell on the left and runs five pixels past the cell on the right, over wherever the next
character lives. Windows draws it that way; the overlap is the font's doing and not anybody's bug.

Hide the cell and you get the complaint this task opened with, exactly: four pixels of black outline standing to the
right of the colour face, with the face sitting where the advance was rather than where the glyph was. The square
the face is painted into is therefore derived from the measured advance rather than from the cell:

```csharp
var advance = after.X - start.X;          // 12 at 9pt, 17 at 13pt — the run's own, so Large follows Large
var em = advance + advance / 3;           // 16 and 22, where the ink measured 15 and 21
return new Rectangle(start.X + 1, start.Y + 1, em, em);
```

One pixel over is the right way to be wrong here. One pixel short leaves black outline on the screen.

**The erase is an ellipse, and that is not decoration.** What has to go under the artwork is the glyph's footprint,
and a face's footprint is round. An ellipse inscribed in that square is the shape of a face; a *rectangle* the same
size — which is what the arithmetic invites — would fill in the corners where the tops of the following letters'
strokes live, and erase them, because the square overflows the advance. The corners stay untouched, the round middle
gets covered, and nothing visible is lost anywhere: everything the ellipse lies on is somewhere the control's own
outline glyph was already drawn.

Checked against an ordinary `RichTextBox` showing the same words, side by side, at rest:

| words | past the patch (outline that survived) | the word after the face |
| --- | --- | --- |
| `🙂` | ours 0 dark, plain box's outline 0 there | — |
| `🙂there` | ours 16 dark, theirs 11 | its ink is still there, more of it even |
| 🙂 at 13pt | ours 0 dark past the patch | — |

The face itself: 75 gold pixels where the outline used to be 13 dark ones, and at 13 points 168 against 0.

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

If it looks wrong somewhere else, the number to re-measure is the ratio in `CellOf`. Fifteen pixels of ink on twelve
pixels of advance is *this* font at *these* sizes, and `TheSquareCoversTheGlyphTheControlActuallyDraws` holds the
square to those two measurements — it fails by four pixels on the cell-sized patch this replaced. A font whose emoji
fits its advance would make the square a little too generous rather than too small, which costs a pixel of the
letter after a face and not a shoulder of outline; the ellipse is what keeps that cost to a corner.
