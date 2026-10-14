using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T12.6: the face in the message box is the colourful one, the same face the transcript shows and the emoji table
/// offers - and not the monochrome outline GDI draws for a COLR font.
/// <para>
/// What may not change is the sentence. An earlier account of this work refused the request because the obvious way
/// to put a picture in a RichTextBox - an RTF <c>\pict</c> group - does not put a picture in an editable one: it puts
/// a **space** (measured: <c>0x20</c>) in <c>.Text</c>, so the count, the caret and the wire all go on talking about a
/// character that is not the emoji anybody typed. That is not a worse-looking message, it is a different message.
/// </para>
/// <para>
/// So the character stays exactly where it is and the face is *painted over it*: the box hides the outline glyph under
/// a patch of what was behind it and draws Windows' own artwork on top. Everything else about the box - 500
/// characters, one face being one of them, what gets sent - is untouched, because nothing about the text was touched.
/// </para>
/// <para>
/// <b>Why these tests paint rather than photograph.</b> A bitmap of the control was tried first and measured:
/// <c>DrawToBitmap</c> on a RichEdit returns a picture with almost none of its glyphs in it (31 dark pixels for a
/// line of text), so "look, a colour face" from a screenshot would be a test of RichEdit's WM_PRINT rather than of
/// this code. <see cref="FaceOverlay.Paint"/> is the method the control's own paint calls; the tests call
/// the same method into a bitmap of their own, and <see cref="TheBoxPaintsItsOwnFaces"/> pins the two together.
/// </para>
/// </summary>
public class ColorFacesInTheBoxTests
{
    private const string Face = "\U0001F642";

    /// <summary>Man, woman and girl joined with zero-width joiners: one text element, one face.</summary>
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";

    /// <summary>What a bitmap is cleared to before the overlay paints: a grey that is neither ink, nor paper, nor colour.</summary>
    private static readonly Color Beneath = Color.FromArgb(0x80, 0x80, 0x80);

    [Fact]
    public void TheBoxDoesNotInkTheOutlineGlyphUnderAFace()
    {
        // "for input widget when render emoji, should only render the colorful icon, not the black white icon ... not
        // render the black white icon only put a empty placeholder then render the colorful icon over the empty
        // placeholder." The box has let RichEdit draw the monochrome outline since the first colour face, and has
        // painted over it. That is fine until the two disagree by a pixel - the outline is square-ish and the patch
        // over it is round, so what survives at the edges is exactly the black that was meant to be gone.
        // So the character stays: one text element, counted, sent, caret-able. What changes is that the box no longer
        // inks it. The placeholder is empty because nothing was drawn in it.
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"hi {Face} there";

            Assert.Equal(box.BackColor.ToArgb(), InkAt(box, 3).ToArgb());
        });
    }

    [Fact]
    public void WordsBesideAFaceAreStillInkedInTheColourTheyWereTypedIn()
    {
        // The blank belongs to faces and to nothing else: a box that whitened its own words would be a box with an
        // invisible sentence, which is the same report pointed in the opposite direction.
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"hi {Face} there";

            Assert.Equal(box.ForeColor.ToArgb(), InkAt(box, 0).ToArgb());
            Assert.Equal(box.ForeColor.ToArgb(), InkAt(box, 6).ToArgb());
        });
    }

    [Fact]
    public void PuttingColourOnAStretchThatHasAFaceInItLeavesTheFaceBlank()
    {
        // Writing a style in this box writes the colour of every character in it, which is what puts the outline back
        // under a face that was blank a moment ago. Formatting a sentence and then losing the blank in its middle is
        // the same fault as never having blanked it, and it arrives later, which makes it worse.
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"say {Face} now";
            box.Select(0, box.TextLength);

            InputFormatting.ApplyToSelection(box, run => run with { Color = "#C00000" });

            Assert.Equal(Color.FromArgb(0xC0, 0x00, 0x00).ToArgb(), InkAt(box, 0).ToArgb());
            Assert.Equal(box.BackColor.ToArgb(), InkAt(box, 4).ToArgb());
        });
    }

    [Fact]
    public void AFaceInTheBoxClaimsNoColourForTheWire()
    {
        // The blank is furniture, and furniture does not go on the wire: a message whose emoji arrives as "#FFFFFF"
        // says the person painted those faces white, which nobody has ever done or been asked to do. It also costs
        // run boundaries, because a face in the middle of some red text now has a colour that is not its neighbours'.
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"go {Face} now";

            var runs = InputFormatting.RunsOf(box, 0, box.TextLength);

            Assert.NotEmpty(runs);
            Assert.All(runs, run => Assert.Null(run.Color));
        });
    }

    [Fact]
    public void WhatIsTypedAfterAFaceIsInkedNormally()
    {
        // Blankening a character is one thing; blankening the caret that sits behind it is another, and would be
        // invisible typing. Measured on the control, the caret after a blank face reads black - which is worth
        // holding to, because it is not the way a character format is usually expected to behave.
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = Face;
            box.Select(box.TextLength, 0);
            box.SelectedText = "x";

            Assert.Equal(box.ForeColor.ToArgb(), InkAt(box, box.Text.Length - 1).ToArgb());
        });
    }

    [Fact]
    public void TheBoxPaintsAFaceInTheColoursWindowsDraws()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"hi {Face} there";
            box.Select(box.Text.Length, 0);

            var painted = Painted(box);

            Assert.True(Coloured(painted) > 30,
                $"the box's own outline face is still showing: {Coloured(painted)} coloured pixels were painted, " +
                "and an outline emoji has none");

            // A face is not one flat blob either: Windows draws shading, and a patch of one colour would be a stuck
            // paint brush rather than a face.
            Assert.True(Hues(painted) > 4, "the face was painted in one colour, which is not the face");
        });
    }

    [Fact]
    public void ThePatchCoversTheFaceAndNotTheWordsBesideIt()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"hi {Face} there";
            box.Select(box.Text.Length, 0);

            var painted = Painted(box);

            // The control drew "hi" and "there" and the overlay has to leave them to it. "Before" is easy: the face
            // is drawn a pixel inside its own cell and never reaches back over the words that came first.
            foreach (var at in new[] { 0, 1 })
            {
                var cell = CellOf(box, at);

                for (var y = cell.Top; y < cell.Bottom; y++)
                {
                    for (var x = cell.Left; x < cell.Right; x++)
                    {
                        Assert.Equal(Beneath.ToArgb(), painted.GetPixel(x, y).ToArgb());
                    }
                }
            }

            // "After" is not so simple: the erase is a pixel wider than the glyph on every side, to take the
            // antialiasing that feathers out past the ink (see TheSquareCoversTheGlyphTheControlActuallyDraws for
            // what the glyph actually covers). That pixel is the limit of what may be touched in the cells of the
            // words that follow, and the erase is an ellipse rather than a rectangle because a face is round: the
            // corners of the square, where the tops and bottoms of the letters either side live, stay as they were.
            var square = Rectangle.Inflate(FaceOverlay.CellOf(box, 3), 1, 1);

            foreach (var at in new[] { 6, 7, 8, 9, 10 })
            {
                var cell = CellOf(box, at);

                for (var y = cell.Top; y < cell.Bottom; y++)
                {
                    for (var x = Math.Max(cell.Left, square.Right); x < cell.Right; x++)
                    {
                        Assert.Equal(Beneath.ToArgb(), painted.GetPixel(x, y).ToArgb());
                    }
                }
            }
        });
    }

    [Fact]
    public void TheSquareCoversTheGlyphTheControlActuallyDraws()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            // Measured off the screen with a lone face on a line, in this app's own font, with nothing of ours
            // painted on it - ink being whatever differs from the paper of the same box, on the same line. Relative to
            // the cell the character's advance starts in, the outline glyph put ink at:
            //
            //   7pt  x +0..+9   y +2..+11      11pt  x +0..+15  y +3..+18
            //   9pt  x +0..+12  y +2..+14      13pt  x +0..+17  y +3..+20
            //
            // - the advance, square, beginning on the cell's own left edge and sitting a little below its top. (An
            // earlier version of this comment quoted a face "15 across on an advance of 12", measured with a full
            // stop typed after the face; the stop was the extra three pixels, and painting by it put the colour face
            // four pixels wider than the character it was meant to be.)
            //
            // So the square has to be that square and no bigger. A face painted wider than the character it replaces
            // looks exactly like what it is - a picture lying where the character is not - and the erase that goes
            // with it takes the letters either side with it.
            foreach (var (points, inkRight, inkTop, inkBottom) in new[]
            {
                (7f, 9, 2, 11), (9f, 12, 2, 14), (11f, 15, 3, 18), (13f, 17, 3, 20),
            })
            {
                box.Font = new Font("Microsoft YaHei UI", points);
                box.Text = Face;
                box.Select(box.Text.Length, 0);

                var cell = box.GetPositionFromCharIndex(0);
                var advance = box.GetPositionFromCharIndex(2).X - cell.X;
                var square = FaceOverlay.CellOf(box, 0);

                Assert.Equal(advance, square.Width);

                Assert.True(square.Left <= cell.X,
                    $"{points}pt: the ink starts on the cell's own left edge and the square starts at +{square.X - cell.X}");
                Assert.True(square.Right - 1 >= cell.X + inkRight - 1,
                    $"{points}pt: the outline inks to +{inkRight - 1} and the square stops at +{square.Right - 1 - cell.X}");
                Assert.True(square.Top <= cell.Y + inkTop,
                    $"{points}pt: the outline starts at y+{inkTop} and the square starts at y+{square.Y - cell.Y}");
                Assert.True(square.Bottom - 1 >= cell.Y + inkBottom - 1,
                    $"{points}pt: the outline inks to y+{inkBottom - 1} and the square stops at y+{square.Bottom - 1 - cell.Y}");
            }
        });
    }

    [Fact]
    public void TheFaceIsPaintedWhereTheGlyphWas()
    {
        // A face does not fill its own bitmap: there is air round the artwork - three pixels of it at nearly every
        // size measured from 9 to 25, so 12 of a 15, 15 of an 18, 18 of a 22 - and it is not quite the same at each.
        // So the bitmap cannot simply be drawn into the square the glyph occupied - the picture
        // would land inside it and below it, a couple of pixels off the character, which is the complaint this test
        // exists to keep. DrawOf hangs the drawing from the middle of its own ink instead.
        //
        // The numbers below are what the painter actually asks for at 9pt: a square 12 wide, and a bitmap three
        // wider than that, whose ink measured about 12 of the 15.
        var glyph = new Rectangle(40, 6, 12, 12);
        const int Side = 15;
        var ink = new Rectangle(2, 2, 12, 12);

        var drawn = FaceOverlay.DrawOf(glyph, ink, Side);

        // Where the ink of the drawing lands, given the place and scale DrawOf chose.
        var scale = drawn.Width / Side;
        var lands = new RectangleF(drawn.X + (ink.X * scale), drawn.Y + (ink.Y * scale), ink.Width * scale, ink.Height * scale);

        Assert.Equal(glyph.X, lands.X, 1);
        Assert.Equal(glyph.Y, lands.Y, 1);
        Assert.Equal(glyph.Width, lands.Width, 1);
        Assert.Equal(glyph.Height, lands.Height, 1);

        // And the scale stays near one, because the bitmap is asked for at about the size it is shown: a face in a
        // message box is rasterised at that size rather than stretched into it, and a stretched face is a blurry one.
        Assert.True(drawn.Width > Side * 0.8f && drawn.Width < Side * 1.2f,
            $"a {glyph.Width} pixel square was filled by stretching a {Side} pixel bitmap to {drawn.Width:0.#}");

        // Air on all four sides, or a bitmap with nothing in it, leaves the square alone rather than dividing by zero.
        Assert.Equal(glyph, FaceOverlay.DrawOf(glyph, Rectangle.Empty, 12));
    }

    [Fact]
    public void ThePaintedFaceIsTheSizeOfTheCharacterItCovers()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"hi {Face} there";
            box.Select(box.Text.Length, 0);

            // The canvas is one flat grey and the overlay paints nothing but the face, so every pixel that comes back
            // not-grey belongs to the picture - and how big that picture is, next to the square the control's own
            // glyph occupies, is the offset complaint in one number. This is the test the previous geometry failed
            // outright: a face painted 16 across over a character 12 wide is a picture sitting where the character
            // is not, however good the picture looks on its own.
            var painted = Painted(box);
            var square = FaceOverlay.CellOf(box, 3);

            var left = painted.Width;
            var top = painted.Height;
            var right = -1;
            var bottom = -1;

            for (var y = 0; y < painted.Height; y++)
            {
                for (var x = 0; x < painted.Width; x++)
                {
                    var p = painted.GetPixel(x, y);

                    // Solidly different from the canvas *and* from the box's own paper. Hiding the outline glyph puts
                    // a patch of the paper down first, which on a grey canvas shows up as a pale ellipse a pixel
                    // wider than the character all round - true of the erase, but not the face, and this test is
                    // about the face. Nor merely different: the artwork's outermost ring is almost transparent and
                    // feathers a pixel further out than anyone can see on white paper.
                    if (Math.Abs(p.R - Beneath.R) + Math.Abs(p.G - Beneath.G) + Math.Abs(p.B - Beneath.B) > 40
                        && Math.Abs(p.R - box.BackColor.R) + Math.Abs(p.G - box.BackColor.G)
                        + Math.Abs(p.B - box.BackColor.B) > 40)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }

            var wide = right - left + 1;
            var tall = bottom - top + 1;

            // A pixel each way for the antialiasing the artwork feathers out with, and no more.
            Assert.True(wide >= square.Width - 1 && wide <= square.Width + 1,
                $"the character is {square.Width} wide and the face painted over it is {wide}");
            Assert.True(tall >= square.Height - 1 && tall <= square.Height + 1,
                $"the character is {square.Height} tall and the face painted over it is {tall}");
            Assert.True(left >= square.Left - 1 && right <= square.Right,
                $"the face runs x {left}..{right} over a character at x {square.Left}..{square.Right - 1}");
            Assert.True(top >= square.Top - 1 && bottom <= square.Bottom,
                $"the face runs y {top}..{bottom} over a character at y {square.Top}..{square.Bottom - 1}");
        });
    }

    [Fact]
    public void TwoFacesOnOneLineEndAtTheSameLine()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            // One line, two faces, two sizes. RichEdit puts every run on a line on the same baseline, and answers
            // "where is this character" with the top of the LINE whatever size the character is - so the only thing
            // that decides how far down a face's square goes is the line's baseline, and the baseline belongs to the
            // tallest ink on the line. A face painted against its own size instead floats above the words beside it
            // and leaves the outline it was meant to be hiding showing underneath it.
            box.Text = $"{Face} {Face}";
            box.Select(3, Face.Length);
            box.SelectionFont = new Font(box.Font.FontFamily, box.Font.Size + TranscriptStyle.LargeDelta);
            box.Select(box.Text.Length, 0);

            Repaint(box);

            var ordinary = FaceOverlay.CellOf(box, 0);
            var large = FaceOverlay.CellOf(box, 3);

            Assert.True(large.Width > ordinary.Width,
                $"the second face was not made larger: {large.Width} against {ordinary.Width}");

            Assert.Equal(large.Bottom, ordinary.Bottom);

            // And the small face has genuinely come down to the line rather than keeping its own offset: on a line
            // of its own size it starts a couple of pixels under the top of the line, and here it starts further
            // down than that, because the line's baseline moved with the large face in it.
            var line = box.GetPositionFromCharIndex(0).Y;
            var own = FaceOverlay.Bottom(ordinary.Width) - ordinary.Width;

            Assert.True(ordinary.Top - line > own,
                $"a face {ordinary.Width} across was painted {ordinary.Top - line} below the top of a line whose " +
                $"tallest ink is {large.Width} across; {own} is where it would sit on a line of its own size");
        });
    }

    [Fact]
    public void FindingItsBaselineLeavesTheCaretAndTheToolbarAlone()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);
            var bold = (Button)Find(form, "btnBold")!;

            // Finding where the baseline of a line is costs a look at what the line is wearing, and a RichTextBox
            // will only say what a stretch of itself looks like if that stretch is highlighted. So the box selects
            // its own line to measure it, and puts the caret back afterwards. What a person is owed is that the
            // detour is invisible: the caret where they left it, and the tools still saying what the caret is in
            // rather than what whichever character the reading happened to be on is.
            box.Text = $"{Face} hello there";
            box.Select(0, Face.Length);
            box.SelectionFont = new Font(box.Font.FontFamily, box.Font.Size, FontStyle.Bold);
            box.Select(box.Text.Length, 0);

            var at = box.SelectionStart;
            var state = bold.BackColor;

            Repaint(box);

            Assert.False(box.Measuring, "the box is still reading itself");
            Assert.Equal(at, box.SelectionStart);
            Assert.Equal(0, box.SelectionLength);
            Assert.Equal(state, bold.BackColor);
        });
    }

    [Fact]
    public void FindingABaselineAsksAboutWordsAndNeverAboutFaces()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            // A line of words answers for itself in one question: what a stretch of itself wears is something a
            // RichTextBox will say, and an ordinary sentence is one stretch.
            var words = Looks(box, "hello there and here and there too and here and here and here and here");

            Assert.True(words <= 2, $"a line of nothing but words was asked about {words} times");

            // A face in the line used to cost a dozen questions, because the control will not call a whole line one
            // font once a face is in it - it borrows another font to draw the face, and reports the line as mixed.
            // Faces are measured instead of asked about, which is both cheaper and more honest: the dear question is
            // the one whose highlight crosses a face, and the answer comes back naming Segoe UI Symbol rather than
            // anything the box was given. Measured: twelve questions and fifteen milliseconds walking this line, two
            // questions now, and the size of a face was always knowable from where the next character starts.
            var sentence = Looks(box, $"{Face} hello there and here and there too and here and here and here");

            Assert.True(sentence <= 4, $"a face in a line of words cost {sentence} questions");

            // And a line of faces alone is never asked at all.
            Assert.Equal(0, Looks(box, string.Concat(Enumerable.Repeat(Face, 12))));

            // Faces are selected, though, and which of the two things that means is still forbidden is worth saying
            // out loud. The box now highlights each face to take its ink off, which is a write and asks the cheap
            // question; what it must never do again is ask a face what it is wearing. More touches than faces is
            // that fault coming back, and twelve faces are inked with twelve touches.
            Assert.Equal(1, TouchesOfFaces(box, $"{Face} hello there and here and there too and here and here and here"));
            Assert.Equal(12, TouchesOfFaces(box, string.Concat(Enumerable.Repeat(Face, 12))));
        });
    }

    [Fact]
    public void FindingABaselineNeverAsksForTheWindowToBePainted()
    {
        Sta.Run(() =>
        {
            // This is the report: "it refreshes many times in a flash" while faces are being typed next to text of
            // two sizes. Asking a RichTextBox what a stretch of itself wears means highlighting that stretch, and a
            // change of selection makes the window draw itself again - and this window paints a face over its own
            // outline glyph on every paint message it is given. Asked from the paint, that is a loop: the reading
            // selected, the selection painted, the paint read, the reading selected, and the answer had not been
            // reached when the first of those paints arrived, so each one asked again. Counted on a line of twelve
            // characters: **346 selections and 31 paints** for a single answer, and every keystroke did it again.
            //
            // Two things ended it. The reading is done when the text or the ink changes, and the paint only reads
            // what was found - so there is no question left to ask in the middle of a paint. And the reading is done
            // with the selection hidden, which leaves it selected (the question is still answered) and leaves the
            // window with nothing to redraw: measured, none - not fewer paints, none.
            using var form = Shown();
            var box = Box(form);

            box.Text = $"{Face} hello there";
            box.Select(3, 5);
            box.SelectionFont = new Font(box.Font.FontFamily, box.Font.Size + TranscriptStyle.LargeDelta);
            box.Select(box.TextLength, 0);

            Repaint(box);

            var before = box.Paints;

            box.RenewInk();

            // Counted the moment the reading finishes, before this test asks for a repaint of its own: what is
            // being denied is that the reading draws anything while it is going on.
            var during = box.Paints - before;

            Assert.True(box.TallestEmOnLine(0) > 0, "the line's ink was never measured at all");

            Assert.Equal(0, during);

            // And the reading has to leave the caret where it found it, since it is the caret a person is watching.
            Assert.Equal(box.Text.Length, box.SelectionStart);
            Assert.Equal(0, box.SelectionLength);
        });
    }

    [Fact]
    public void ASelectedFaceSitsOnTheSelectionRatherThanCuttingAHoleInIt()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"hi {Face} there";
            box.Select(3, 1);
            form.Activate();
            box.Focus();

            // Said out loud because the half below is worthless without it: a selection is only drawn by a box that
            // has the caret, and a test cannot make a window the foreground one on a machine with no desktop.
            Assert.True(box.Focused, "this is about a selected face, and the box never got the caret to select one in");

            // A selected word is blue behind the letters. A patch of the box's white paper over a selected face is a
            // hole punched in the selection, which reads as a rendering fault in the middle of a person's word - so
            // the patch takes the colour that was behind the character, whatever this machine's theme calls it.
            // That colour is sampled off the screen rather than guessed: a RichEdit window's selection is
            // SystemColors.Highlight exactly (FF0078D7 here).
            var painted = Painted(box);
            var patch = Count(painted, SystemColors.Highlight);

            Assert.True(patch > 20, $"the selected face was patched with {patch} pixels of the selection colour " +
                                   "instead of the selection colour itself");
            Assert.True(Coloured(painted) > 30, "the face was not painted over its own selection");

            // With nothing selected the same patch is the box's own paper, not a blue mark left behind.
            box.Select(box.Text.Length, 0);

            Assert.Equal(0, Count(Painted(box), SystemColors.Highlight));

            // And with a selection in a box that has lost the caret it is paper too - which is not a shortcut but
            // what was measured: an unfocused RichEdit window draws no selection at all (the pixel where the
            // highlight was a moment ago reads FFFFFFFF again), so there is no blue to match, and painting blue
            // behind a face nobody is selecting would invent a selection of our own.
            ((ListBox)Find(form, "lstRooms")!).Focus();
            box.Select(3, 1);

            Assert.False(box.Focused, "the second half is about a box without the caret");
            Assert.Equal(0, Count(Painted(box), SystemColors.Highlight));
        });
    }

    [Fact]
    public void AFaceInLargeWordsIsBiggerThanAFaceInOrdinaryOnes()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            Enter(form);

            box.Text = $"say it {Face} bigger";
            box.Select(0, box.Text.Length);
            Pick(form, "btnSize", "Large");
            box.Select(box.Text.Length, 0);

            var large = ColouredBox(Painted(box))!.Value;

            box.SelectAll();
            Pick(form, "btnSize", "Normal");
            box.Select(box.Text.Length, 0);

            var plain = ColouredBox(Painted(box))!.Value;

            Assert.True(large.Width > plain.Width,
                $"a face in large words was painted {large.Width}px and in ordinary ones {plain.Width}px: " +
                "the face is sized by the room its own character takes, which grows with the words round it");
        });
    }

    [Fact]
    public void AFaceWrappedToTheSecondLineIsPaintedOnTheSecondLine()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            // Long enough to wrap, so that the second line's y is not the first line's y plus anything anybody
            // remembered: the positions the control gives are client coordinates, which also go negative when the
            // box is scrolled.
            box.Text = new string('x', 200) + Face;
            box.Select(box.Text.Length, 0);

            var at = box.Text.Length - 2;
            var where = box.GetPositionFromCharIndex(at);

            Assert.True(where.Y > box.Font.Height, "the face did not end up on a second line, so this proves nothing");

            var painted = Painted(box);
            var ink = ColouredBox(painted)!.Value;

            Assert.True(ink.Top >= where.Y - 2 && ink.Bottom <= where.Y + box.Font.Height + 2,
                $"the face was painted at y {ink.Top}-{ink.Bottom} while its character is at y {where.Y}");
        });
    }

    [Fact]
    public void AJoinedFamilyIsOneFaceAndNotThree()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"us {Family} here";
            box.Select(box.Text.Length, 0);

            // Three faces would be three cells' worth of ink, and the run is one element by the ruler the length
            // limit uses (v1.4), so this is the same single character the message carries.
            Assert.Equal(1, Count(box));
            Assert.Equal(1, ValidationRules.Length(Family));
        });
    }

    [Fact]
    public void WordsWithNoFaceInThemAreLeftAlone()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = "ordinary words, and a ế which is a letter and not a face";
            box.Select(box.Text.Length, 0);

            Assert.Equal(0, Count(box));
            Assert.Equal(0, Coloured(Painted(box)));
        });
    }

    [Fact]
    public void PaintingLeavesTheSentenceExactlyAsItWas()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"hi {Face} there";
            box.Select(3, 1);

            var before = box.Text;
            var elements = ValidationRules.Length(before);

            Painted(box);

            // This is the whole reason the face is paint. A picture in the text would have made .Text say something
            // else, and then the count, the caret and the wire would each be describing a different sentence: eleven
            // UTF-16 units, nine letters, a space either side and one face in the middle.
            Assert.Equal(before, box.Text);
            Assert.Equal(11, box.Text.Length);
            Assert.Equal(10, elements);
            Assert.Equal(10, ValidationRules.Length(box.Text));
            Assert.Contains(Face, box.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TheBoxPaintsItsOwnFaces()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = $"hi {Face} there";
            box.Select(box.Text.Length, 0);

            Repaint(box);

            // The tests above call the painter; this says the control calls it too, which is the only reason those
            // measurements are about the window a person looks at.
            Assert.Equal(1, box.PaintedFaces);

            box.Text = "no face here";
            Repaint(box);

            Assert.Equal(0, box.PaintedFaces);
        });
    }

    [Fact]
    public void AnEmptyBoxPaintsTheWordThatSaysWhatItIsFor()
    {
        Sta.Run(() =>
        {
            using var form = Shown();
            var box = Box(form);

            box.Text = string.Empty;
            Repaint(box);

            // This word has been in the code since v1.7 and was never on the screen. It was drawn in an OnPaint
            // override, and WinForms does not call OnPaint for a RichTextBox - the paint message arrives at the
            // window procedure and nothing invokes the override. The test that was supposed to cover it asked the
            // ShowsHint property, which is a fact about the text and not about the window, so it passed the whole
            // time. PaintedHint is the same claim made about the paint.
            Assert.True(box.ShowsHint);
            Assert.True(box.PaintedHint, "the box says it shows a hint and its own paint did not draw one");

            box.Text = "a word";
            Repaint(box);

            Assert.False(box.PaintedHint, "the hint is still being drawn over somebody's sentence");
        });
    }

    // ------------------------------------------------------------------------------------------ helpers

    private static MessageInput Box(Control form) => (MessageInput)Find(form, "txtMessage")!;

    private static Control? Find(Control root, string name)
    {
        if (root.Name == name)
        {
            return root;
        }

        foreach (Control child in root.Controls)
        {
            if (Find(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>A window on screen with a room entered: the box is shut until there is somewhere to send.</summary>
    private static Form Shown()
    {
        var form = new Lantern.UI.HostForm(new Lantern.Core.FakeChatService("Haitao"));

        form.Show();
        Enter(form);

        return form;
    }

    private static void Enter(Control form) => ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;

    private static void Pick(Control form, string tool, string item) =>
        ((Button)Find(form, tool)!).ContextMenuStrip!.Items.Cast<ToolStripMenuItem>().Single(i => i.Text == item)
            .PerformClick();

    /// <summary>What the box's own paint would put on screen, on a bitmap of the box's client area.</summary>
    /// <summary>
    /// How many times the box highlights a piece of itself in order to say how tall the ink on its first line is -
    /// the one question about itself that a RichTextBox has to be asked, and the one that can quietly cost more than
    /// the painting it is serving.
    /// </summary>
    private static int Looks(MessageInput box, string text) => Asking(box, text, aboutFaces: false);

    /// <summary>How many times the box highlights a face - which, since the blank, is once per face and no more.</summary>
    private static int TouchesOfFaces(MessageInput box, string text) => Asking(box, text, aboutFaces: true);

    private static int Asking(MessageInput box, string text, bool aboutFaces)
    {
        // Setting the text measures the box too, which is the point of setting it that way: the reading belongs to
        // the change rather than to the paint, so this counts the reading asked for on its own.
        //
        // It is set twice, and the reason is the box's own economy: since a change only puts the ink of the piece
        // that changed back to rights, a line typed after a line would leave the earlier faces untouched and make
        // this look cheaper than it is. Empty first, and the whole line is new.
        box.Text = string.Empty;

        var at = box.SelectionStart;
        var length = box.SelectionLength;

        var looks = 0;

        void Counted(object? sender, EventArgs e)
        {
            var selected = box.SelectedText;

            // Highlighting a face to take its ink off is not asking it what it is wearing, and the two have to be
            // counted apart: the dear question is the one whose highlight crosses a face and then asks the control
            // about its font, which costs an order of magnitude more and answers nothing truthfully.
            if (selected.Length > 0 && ColorEmoji.IsEmoji(selected) == aboutFaces)
            {
                looks++;
            }
        }

        box.SelectionChanged += Counted;

        try
        {
            // The line arriving is what is measured, not a reading asked for afterwards: a box that has already
            // looked at a face has nothing left to say about it, and this is about what arriving costs.
            box.Text = text;
        }
        finally
        {
            box.SelectionChanged -= Counted;
            box.Select(at, length);
        }

        return looks;
    }

    /// <summary>
    /// What the control says the character at <paramref name="at"/> is inked with, with the whole of its text element
    /// highlighted rather than one UTF-16 unit of it.
    /// </summary>
    private static Color InkAt(MessageInput box, int at)
    {
        var start = box.SelectionStart;
        var length = box.SelectionLength;

        try
        {
            box.Select(at, System.Globalization.StringInfo.GetNextTextElement(box.Text, at).Length);

            return box.SelectionColor;
        }
        finally
        {
            box.Select(start, length);
        }
    }

    private static Bitmap Painted(RichTextBox box)
    {
        var bitmap = new Bitmap(box.ClientSize.Width, box.ClientSize.Height);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Beneath);
            FaceOverlay.Paint(box, graphics);
        }

        return bitmap;
    }

    /// <summary>
    /// Asks for a paint and lets it happen. There is no message loop in a test, so the paint message has to be
    /// pumped: <c>Update()</c> alone was measured not to produce a paint message for this control at all, which is
    /// how the test above first read nought and looked like a broken overlay rather than a test that had not asked.
    /// </summary>
    private static void Repaint(Control box)
    {
        box.Refresh();
        Application.DoEvents();
    }

    /// <summary>How many faces the overlay finds and positions, without caring where the ink lands.</summary>
    private static int Count(RichTextBox box)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);

        return FaceOverlay.Paint(box, graphics);
    }

    /// <summary>The cell the control gives a character: from where it starts to where the next one does.</summary>
    private static Rectangle CellOf(RichTextBox box, int at)
    {
        var here = box.GetPositionFromCharIndex(at);
        var next = box.GetPositionFromCharIndex(at + 1);

        return Rectangle.FromLTRB(here.X, here.Y, next.X, here.Y + box.Font.Height);
    }

    /// <summary>How many pixels are exactly this colour.</summary>
    private static int Count(Bitmap bitmap, Color colour)
    {
        var count = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == colour.ToArgb())
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int Coloured(Bitmap bitmap)
    {
        var count = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);

                if (p.A > 0 && Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) > 40)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int Hues(Bitmap bitmap)
    {
        var seen = new HashSet<int>();

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);

                if (p.A > 0)
                {
                    seen.Add((p.R << 16) | (p.G << 8) | p.B);
                }
            }
        }

        return seen.Count;
    }

    /// <summary>The rectangle the coloured pixels cover, or null when there are none.</summary>
    private static Rectangle? ColouredBox(Bitmap bitmap)
    {
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);

                if (p.A > 0 && Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B)) > 40)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < 0 ? null : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }
}
