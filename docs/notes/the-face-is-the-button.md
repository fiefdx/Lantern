# The face is the button, and the button is not as big as it looks

Two of the tools above the message box used to be words with a small picture next to them: **🙂 Emoji** and
**Share**. Now one is a face and the other is a sheet of paper, and getting there produced a measurement worth
writing down, because the complaint "the emoji is cut off" was not about the button at all.

## The face was being sawn off, everywhere, at every size

`ColorEmoji.Render` draws one emoji into a square cell: an 18×18 bitmap, an em of 18, the baseline at 0.88 of the
cell "because descenders would otherwise be cut off". That sentence is reasonable and it was wrong, and the way it
was wrong was found by counting pixels:

| face | cell | ink covered | verdict |
| --- | --- | --- | --- |
| 🙂 | 16×16 | (2,0)–(15,15) | touches the top, right and bottom |
| 🙂 | 18×18 | (2,0)–(17,17) | same, at every size tried |
| 🙂 | 20, 22, 24, 28 | edge, edge, edge, edge | same |
| 😀 👍 👨‍👩‍👧 🎉 | all of the above | edge | same |
| 🇻🇳 | 16–28 | (0,6)–(15,13) | runs off **both** sides |

Every face reached the last row and the last column of its cell. A flag reached the left edge and the right edge too.
That is not the button clipping a picture; that is the rasteriser drawing the picture outside the paper it was
drawing on. The button then clipped what survived, which is why the toolbar showed the complaint most clearly.

**An emoji's ink box is not its em box.** `seguiemj.ttf` is a COLR/CPAL font, and what its metrics report describes
the *typeface* — one ascent and descent for all of it — rather than the coloured picture of one emoji. Asking the
font where the artwork goes is asking the wrong person. There is also no baseline offset that fixes this: the ink is
taller than the em it was drawn at, so sliding it up moves the cut from the chin to the hair.

So the ink is now measured rather than predicted. A probe canvas four times the em — too big to clip anything — gets
one draw; its pixels are scanned for the bounding box of "not transparent"; and the real draw into the cell is a
scaled, centred copy of that. Two pixels of the arithmetic are not obvious:

* **One pixel of air** between the ink and the edge of the cell, because ink stopping exactly on the last row reads
  as a line sawn across the face.
* **One pixel of overhang** allowed outside the measured box before fitting, because the measurement calls a pixel
  inked when it is only *partly* covered — the shape continues into the pixel beyond it, and antialiasing is exactly
  that continuation. Fitting the measured box exactly is what left a hairline of sawn-off antialiasing on one edge.
* The fit **never scales up**. A flag is 16×6: blowing it up to fill an 18×18 cell would make that flag fatter than
  the same flag in the message beside it. `min(width, height)` of the two scales, capped at 1.

The cost is honest and small: a face in a cell of N is now about 11% smaller than it was, and complete. The cell
itself stays exactly N×N, because the line a face sits in is sized by that number and a picture that came out a
different size than it was asked for would move the line under the sentence.

One raster and one scan per face per size, and `Png`'s cache means it happens once — which is also why the scan is
allowed to be a simple double loop with `GetPixel`.

The same method draws the toolbar face, every tile in the emoji picker and every face in a received message, so this
one fix is three fixes. `AFaceIsDrawnWholeWhereverItIsDrawn` measures six faces at four sizes and fails if any ink
lands within a pixel of a cell edge.

## A Button has no DisplayStyle, and cuts a picture without a word

The obvious way to make a button "just the picture" is `DisplayStyle = Image`. There isn't one: `DisplayStyle` is a
`ToolStripItem` thing. A `Button` draws its `Image` and its `Text`, and the whole of the instruction "picture only"
is `Text = ""`.

The other half is that **WinForms neither scales a button's image nor grows the button**. It draws what it is given
and the parts outside the button are not drawn, silently — there is no warning, no ellipsis, no clue: a smile simply
arrives as a chin. So the picture size is chosen against the button's own client area
(`ComposerStyle.PicturePixels`: the smaller side less a pixel of air at each edge), and
`AssertWhole` in the tests fails any tool button holding a picture bigger than the space showing it. That test is
the difference between "it looked cut on my machine" and a test that says which button and by how much.

For the same reason the emoji tool falls back to the **word** when `ColorEmoji.Glyph` returns null: on a machine
without Windows' emoji font the face cannot be drawn, and a button with no picture and no text is a button nobody can
find. The words survive on every machine as `AccessibleName` and as the tooltip, which are the two ways of asking
what a button does that a picture cannot answer.

## The share tool's page is eight lines of geometry

`ToolIcons.Page` draws a sheet with its corner folded — the shape every file listing in the world has meant by "a
file" since there were file listings. Drawn rather than shipped as a resource for three reasons: it takes
`SystemColors.ControlText`, so it is still visible on a theme nobody has looked at; it comes out sharp at whatever
size and scale the button is; and eight lines of geometry can be read in a review, which a `.png` cannot. The fold is
drawn as two extra lines because a rectangle with a corner missing is a rectangle with a corner missing, and it is
the fold that says paper. The two lines of "writing" inside are only drawn at 22px and above, where two lines are
still two lines; at 18 they merge into a smudge across the page, which reads as dirt.

## Size and colour are menus now, and the arrow on them is checked

They used to cycle: click **Aa** for the next size, click **A** for the next colour. Cycling was cheap to build and
it had two costs. Five of the six colours were behind a click nobody thought to make, so five of six were
undiscoverable; and there was no way to see which size was currently chosen without clicking round through all
three. Both tools now open a menu — the whole list at once, each size written at its own size, each colour written
in itself, with a tick on the choice the caret is sitting in — while bold, italic and underline stay one-click
toggles, because those three are the ones that switch on and off.

Both menus are *built* from `ComposerStyle.Sizes` and `ComposerStyle.Palette` rather than written out item by item,
so what is on offer and what is listed cannot be edited separately and then disagree — the disagreement being a menu
saying **Green** over a colour that arrives red. The palette is now a list of `Ink(Name, Color)` for the same
reason: a colour list and a separate list of colour names is two things that can drift.

One trap in testing this, because it is easy to fall into again: **a menu pick on a box that is shut does nothing.**
With no room entered the message box is disabled, the item handler returns, and a test that then measures the tool's
font measures the same 9 points it started with and reports success. A test about these menus has to enter a room and
put words in the box first — otherwise it is a test of the initial state, wearing the name of a test of the change.

And the ▼ that says "this one opens something" is checked against the font's own glyph table. A code point the font
has no glyph for comes back as `.notdef` — a replacement box on a toolbar, meaning nothing at all, roughly the width
of the arrow it replaced. `TheArrowSayingAToolOpensAMenuIsAGlyphTheFontHas` asks `SKFontManager` rather than assuming
that Segoe UI has U+25BC, which is the kind of thing that is only true until the day the form's font is changed.
