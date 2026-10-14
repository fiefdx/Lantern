# The right third of a photograph is still the photograph

Reported after the v1.7 work, and nothing to do with any of it: **"not every time I double click a image shared in
the transcript can open the image viewer window, sometimes there is no reaction at all."**

Sometimes. That word is the whole bug, and it took measuring to find out which times.

## What a photograph is, to the control

A shared picture in the transcript is a headline, a caption, and a thumbnail. The thumbnail is an inline picture, and
an inline picture in a RichTextBox is - to everything the control will tell you about the document - **one character**.
So the whole affordance has always rested on one question: *which character is this click on?*

```
private void RtbChat_MouseDoubleClick(object? sender, MouseEventArgs e)
    => TryOpenPictureAt(rtbChat.GetCharIndexFromPosition(e.Location));
```

and `TryOpenPictureAt` asks whether that character index falls inside the span the entry was written into, which is
the right rule and is documented as being deliberately strict: a photograph that opens when nobody pointed at it is
worse than one that does not open.

An entry's span ends **immediately after its thumbnail** - there is nothing else after a thumbnail but the line break
that opens the next entry.

## Where the line is

A real transcript, a real 140 × 90 photo, and `GetCharIndexFromPosition` asked about a grid of clicks across the
thumbnail. The photo's own character is 158; 159 is the character after it, which is one past the end of the entry:

| click, relative to the photo's top-left | reported character | opened? |
| --- | --- | --- |
| +2, +45 (left edge) | 158 | yes |
| +35, +45 | 158 | yes |
| +70, +45 (the middle) | 158 | yes |
| **+105, +45** | **159** | **nothing** |
| **+138, +45** (near the right edge) | **159** | **nothing** |
| +70, +100 (below the photo) | 159 | nothing |

Same for every row of the photo: the control answers for a picture up to somewhere past its middle, and past that it
answers with the **next** character. So the left two thirds of a photograph opened the viewer, and the right third did
not - and a person cannot see that line, and cannot aim away from it, because it is not a line in the photograph. It is
a line in how the control divides the picture's box up.

There is a test that has been double-clicking a thumbnail since v1.3.1, and it aims at the middle: `+70, +45`. It has
been passing the whole time. The middle is on the answering side of a boundary nobody knew was there.

## What the answer is not

The obvious fix is to accept the character after the entry as well - `caret <= span.End` - and it is wrong, twice over.

It is wrong because the character after a thumbnail is the line break before the next entry, and clicks in the empty
band *under* a tall thumbnail report that character too (last row of the table). A thumbnail makes its own line as tall
as itself, so there is a strip of empty transcript under every photograph that belongs to no words at all. Accepting
the character after would open the photograph from that strip, which is the same fault pointed the other way: the
program doing something to a photograph nobody pointed at.

It is wrong because it is arithmetic about characters when the question is about **pixels**. The question a double click
asks is "is that click on that photograph", and there is exactly one authority on that, which is where the photograph
is drawn and how big it is drawn.

## The span remembers the box

So the entry now remembers, as well as the characters it was written into:

- `Cell` - the picture's own character, or -1 when there was no decodable image and therefore no picture to hit;
- `Across` and `Down` - the box in pixels that the picture was asked to fill.

Both are knowable **once**, in the moment the thumbnail is written, which is why they are recorded there rather than
worked out later: working it out later means decoding the photograph again to measure it, on the click path, on the
way to opening a picture the person has just said they want to see. So `RtfPicture.InlineFor`, which used to hand back
a string, hands back the string *and the size it asked for* - because whoever puts a photograph into a transcript is the
same person who has to know where its edges are.

The box is in the twips-per-pixel units the RTF is written in, so it is scaled by the screen's dots per inch before it
is compared with a click (`rtbChat.DeviceDpi / 96`) - and the hit area is the **photograph**, not the line it sits on,
which is what keeps the empty band under it doing nothing. `ADoubleClickBelowAThumbnailOpensNothing` is the test for
that half, and it was written before the fix so that the fix could not buy its way past it.

## The menu had the same hole, quietly

The right-click menu over a photograph goes through the same character index, so **Save picture to…** could not be
reached from the right third of a photo either. It was never reported, because the menu's rule has always been the
generous one - a menu that appears wherever an entry's line still is is a kindness - and the band under a thumbnail is
inside its line, so the menu usually appeared anyway and the hole was invisible. It is the same fault; it gets the same
answer, and now `PictureAtPixel` sits behind both.

## What is guaranteed by a test

- **`ADoubleClickOnTheFarSideOfAThumbnailOpensTheViewer`** - `+136, +45`, which is in the dead third of a 140 pixel
  photo. This is the report, and it failed before the change.
- **`ADoubleClickBelowAThumbnailOpensNothing`** - under the photo, with a second entry below it so the click has a real
  character to be reported. Stops the fix from becoming `caret <= span.End`.
- **`ADoubleClickInTheMiddleOfAThumbnailOpensTheViewer`** (v1.3.1) - the middle, still passing, and worth keeping
  precisely because it is where the old rule ended.
- **`ADoubleClickOnALineThatIsNotAPictureOpensNothing`** - the words of an ordinary message stay ordinary.

## What is left to eyes

The band under a tall photograph, and the couple of pixels at its far edge, are a judgement: the photograph's own box is
the rule, and a click one pixel outside it does nothing. That is the honest boundary rather than a comfortable one, but
it is still a boundary, and only a person clicking can say whether it is where they expected it to be.
