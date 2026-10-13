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

            // "After" is not so simple, and the reason is measured rather than argued: the emoji this font supplies
            // is drawn wider than the advance it occupies, three pixels past it at 9pt (see
            // TheSquareCoversTheGlyphTheControlActuallyDraws). Hiding that glyph therefore covers a column or two of
            // the next character's cell - which is where the outline was drawn anyway, so nothing visible is lost.
            // What must not happen is the erase running past the face, so the limit is the face's own square, and the
            // erase is an ellipse because a face is round: the corners of that square, where the tops of letters are,
            // are left exactly as they were.
            var square = FaceOverlay.CellOf(box, 3);

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
            // painted on it. Relative to the cell the character's advance starts in, the control's outline glyph put
            // ink at x +1..+15 and down to y +14 at 9pt, and at x +1..+21 and down to +22 at 13pt - in cells 12 and
            // 17 wide. The cell is the advance; the glyph is an em, and an em here is a third wider than the advance
            // again.
            //
            // Hide the cell and the right shoulder of the black outline stays on screen beside the colour face, with
            // the face sitting where the advance was rather than where the glyph was. That is the complaint this
            // task was opened with, and it is why the square below is asked for instead of the cell.
            foreach (var (points, inkRight, inkBottom) in new[] { (9f, 15, 14), (13f, 21, 22) })
            {
                box.Font = new Font("Microsoft YaHei UI", points);
                box.Text = Face + ".";
                box.Select(box.Text.Length, 0);

                var cell = box.GetPositionFromCharIndex(0);
                var advance = box.GetPositionFromCharIndex(2).X - cell.X;
                var square = FaceOverlay.CellOf(box, 0);

                Assert.True(square.Left <= cell.X + 1,
                    $"{points}pt: the ink starts one pixel into the cell and the square starts at +{square.X - cell.X}");
                Assert.True(square.Right - 1 >= cell.X + inkRight,
                    $"{points}pt: the outline inks to +{inkRight} and the square stops at +{square.Right - 1 - cell.X}");
                Assert.True(square.Bottom - 1 >= cell.Y + inkBottom,
                    $"{points}pt: the outline inks to y+{inkBottom} and the square stops at y+{square.Bottom - 1 - cell.Y}");

                // Wide enough for the glyph and no wider: whatever this square covers, the erase covers too, so a
                // square bigger than the glyph would start rubbing out the letter behind it for nothing.
                Assert.True(square.Width <= advance + advance / 2,
                    $"{points}pt: a square {square.Width} wide on a {advance} advance reaches too far into the next character");
            }
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
