# The face that came out in colour

*v1.5 — why the transcript draws emoji as pictures, and what that costs.*

## What was reported

v1.4 shipped emoji that were counted correctly and travelled intact, and closed with a checklist question for a
human: *does your Windows paint them in colour or in outline?* The answer came back: **outline, in the message box
and in the transcript both.** The faces on screen were not the faces in the picker.

## What was measured first

Before touching anything, the claim "GDI has no colour-glyph path" was tested rather than repeated. `GetGlyphOutline`
against **Segoe UI Emoji** for `U+1F600`, asked three ways:

| requested format | bytes needed | colour key |
| --- | --- | --- |
| `GDI_UNICODECHAR` + `GGO_GRAY8_BITMAP` | 0 | `0x000000` |
| `GGO_COMPOSITABLE` | 0 | `0x000000` |
| `GGO_INCREMENTAL` | 0 | `0x000000` |

Nothing at all. Not a small bitmap, not a grey one — an empty answer, which is the shape of a stack that has no idea
what a coloured layer is. Microsoft's own colour-font documentation agrees from the other side: colour fonts
(COLR/CPAL, SVG, CBDT) are rendered through Direct2D, and the GDI-compatible text interfaces do not do colour glyphs.

WinForms `TextBox` and `RichTextBox` are GDI controls. No font choice, no `SelectionColor`, no `CharacterCasing`
trick changes that. **The outline faces were not a setting; they were the ceiling of the control.**

## Where the colour actually lives

`C:\Windows\Fonts\seguiemj.ttf` is Windows' own emoji font — the one the **Win + .** popup draws — and it is a
COLR/CPAL font: each face is a stack of monochrome layers plus a palette. Measured on this machine, rasterised by
Skia:

| element | glyph | ink pixels | coloured pixels | distinct shades | sample |
| --- | --- | --- | --- | --- | --- |
| 😀 `U+1F600` | 11680 | 500 | 695 | 63 | `#FFC83D` |
| 🎉 `U+1F389` | 4305 | 580 | 338 | 101 | `#0081A7` |
| 🔵 `U+1F535` | 3475 | 1064 | 805 | 47 | `#0078D7` |

So the artwork and its colours were on the machine the whole time. What was missing was a rasteriser with a palette.

Three ways to get one were on the table:

* **hand-written Direct2D/DirectWrite interop** — no new package, but it means declaring `ID2D1Factory`,
  `ID2D1DCRenderTarget` and `IDWriteFactory` vtables by hand. Those orders are not published on MSDN, and a slot
  wrong by one is an access violation at a distance. A few hundred lines, and the failure mode is a crash in the
  chat window;
* **ship an emoji PNG set** (Noto/Twemoji) — no native code at all, but it is *different artwork* from what the
  picker showed, it is several megabytes in the repository, it needs attribution, and any emoji outside the set has
  no picture;
* **SkiaSharp** — one package that restores cleanly for `net10.0-windows`, ~30 lines of real code, and it reads the
  font Windows already has, so the face is the face that was picked.

The third was taken. It is the first graphics dependency in the project and it earns its place by being the only one
of the three that both stays honest about the artwork and stays small in code.

## How it is put on screen

The transcript already carried inline PNG pictures — photo thumbnails since v1.2, a page icon per shared file since
v1.3 — so the machinery for "a picture sitting inside a line of a RichTextBox" existed and was already tested.
`ColorEmoji` produces a PNG of one text element, at the height of the line it will sit in
(`Math.Max(18, rtbChat.Font.Height)`), and hands it to `RtfPicture.Inline`. `ChatForm` writes the content of an
entry by running it through `ColorEmoji.Split`, which cuts a sentence between words and faces and never between the
halves of a surrogate pair.

Three rules in it are worth their weight:

* **one picture per text element.** A cell is square because an emoji is square. Feeding a run of two faces into one
  cell draws the second one outside the bitmap: it would be typed, counted, sent — and never seen. The first version of
  this code did exactly that, before it reached a screen; `TwoFacesInARowAreTwoPicturesNotOneSquashedOne` is there so
  that it stays a thing that was thought about rather than a thing that was noticed once.
* **`IsEmoji` is the emoji ranges, not "above ASCII".** `ê` and `ế` are outside the Basic Multilingual Plane too. A
  Vietnamese sentence turned into a strip of pictures would have looked like a colour win and been a catastrophe.
* **all or nothing, per run.** If one element in a run cannot be rasterised, the run is written as text instead. Half
  a sentence in colour and half in outline, with nothing to say why, reads as a rendering fault.

Everything falls back to plain text — no font, no renderer, no glyph for that element — because an outline face is a
worse-looking message and a lost message is a bug.

## What it costs

A picture is not a character, and this is the part that used to be invisible:

* `rtbChat.Text` no longer contains the faces, so **selecting the transcript and copying it out loses them**, and
  searching the control's text cannot find them;
* a face in a message is a **picture in the control's text stream**, which is the same stream the right-click hit
  tests of v1.3.1 walk. That is why there is a test with a shared file and a face in the same room asserting they
  are still two distinguishable pictures at two different sizes;
* **the box is still outline.** A classic edit control cannot contain an image at all — colour there means replacing
  the control with something image-capable, and that touches Enter-to-send, `MaxLength`, the caret and the picker
  itself. It is a decision, not a tweak, so it was left to be decided rather than done quietly;
* the two v1.4 tests that looked at an emoji's cell in the transcript **had to be re-expressed**, not deleted: a body
  emoji no longer has a cell in the control's text. They now look at a face written as text — in a headline — which
  is where the "is anything drawn at all, and is it not the missing-glyph box" promise still lives.

## What is guaranteed by a test, and what is left to eyes

Tests guarantee: the font read is Windows' own; a rasterised face has real hue and more than eight shades in it; a
face is not `ê`; a joined family is one element and one picture; two faces are two pictures; the same face twice is
one raster; a body emoji reaches the transcript as one embedded picture at line height; a headline emoji is still
text; a file's page icon and a face are different pictures; and the message that leaves this window is exactly what
was typed, faces included.

One thing no test can settle is what a person sees when the room is full of faces at 18 pixels on a laptop in
daylight — whether they are the right size, whether the line they sit on is still comfortable, and whether the other
machine, running v1.5 over a real Bluetooth link, shows them in colour too. That is
`docs/testing/v1.5-colour-emoji-checklist.md`.
