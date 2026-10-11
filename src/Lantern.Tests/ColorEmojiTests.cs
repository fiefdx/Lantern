using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Colour emoji: who rasterises them, what gets turned into a picture, and what the transcript looks like as a
/// result.
/// </summary>
/// <remarks>
/// <para>
/// v1.4 counted emoji correctly and proved they survived the wire, and left the display question to a person with
/// eyes - who came back and said the faces were black and white in both the box and the transcript. That is not a
/// setting. WinForms draws text with GDI, and GDI has no colour-glyph path: asked for the picture of
/// <c>U+1F600</c> it hands back nothing at all (measured here: <c>GetGlyphOutline</c> reports 0 bytes needed for
/// GRAY8, COMPOSITABLE and INCREMENTAL alike). Windows' emoji are COLR/CPAL - coloured layers plus a palette - and
/// resolving those layers is DirectWrite-class work, which is why Microsoft's own GDI-compatible text interfaces say
/// they do not do colour fonts.
/// </para>
/// <para>
/// So a face is no longer drawn as text where a message is read: it is rasterised by Skia from
/// <c>C:\Windows\Fonts\seguiemj.ttf</c> - the very font the <b>Win + .</b> popup reads, so the artwork is the
/// artwork that was chosen - and put into the line as one of the inline pictures the transcript already carried for
/// photos and file pages.
/// </para>
/// <para>
/// <b>The cost, stated where it can be read.</b> A picture is not a character. Once a face is a picture it is no
/// longer in <see cref="RichTextBox.Text"/>, so selecting the transcript and copying it out loses the faces, and
/// searching the control's text cannot find them. What was typed is still held whole by the room and still travels
/// whole over Bluetooth - the tests below keep both halves in view rather than pretending the trade is free.
/// </para>
/// </remarks>
public sealed class ColorEmojiTests
{
    private const string Smiley = "\U0001F600";                                   // 😀
    private const string Party = "\U0001F389";                                     // 🎉
    private const string Heart = "\u2764";                                         // ❤
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";     // 👨‍👩‍👧
    private const string Flag = "\U0001F1FB\U0001F1F3";                             // 🇻🇳

    // ------------------------------------------------------------------ the raster

    [Fact]
    public void TheFontBeingReadIsTheOneWindowsItselfUsesForEmoji()
    {
        // Not "a" colour font - the one the emoji picker draws, so a face chosen from that popup is the face that
        // turns up in the room. If this ever fails, the machine has no Segoe UI Emoji and Lantern quietly falls
        // back to outline text; nothing breaks, it just stops being colourful.
        Assert.True(File.Exists(ColorEmoji.FontPath), $"no emoji font at {ColorEmoji.FontPath}");
        Assert.True(ColorEmoji.Available, "Skia could not open the emoji font on this machine");
    }

    [Fact]
    public void AFaceComesOutInColourRatherThanAsAnOutline()
    {
        var png = ColorEmoji.Png(Smiley, 32);

        Assert.NotNull(png);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png![..4]);

        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);

        var coloured = ColouredPixels(bitmap, threshold: 40);
        var shades = Shades(bitmap);

        // Both numbers, because each one alone lies. "Ink" is true of an outline face; "many colours" is true of a
        // grey photograph. A yellow face is ink that is also, in numbers, not grey.
        Assert.True(coloured > 60, $"only {coloured} pixels of a 32 pixel face have a real hue in them");
        Assert.True(shades > 8, $"the face came out in only {shades} distinct shades");
    }

    [Fact]
    public void TheSameFaceTwiceIsRasterisedOnce()
    {
        // A room full of the same face is one raster, not two hundred: the cache is keyed by element and size.
        Assert.True(ReferenceEquals(ColorEmoji.Png(Smiley, 24), ColorEmoji.Png(Smiley, 24)));
        Assert.False(ReferenceEquals(ColorEmoji.Png(Smiley, 24), ColorEmoji.Png(Smiley, 32)));
    }

    // ------------------------------------------------------------------ what counts as a picture

    [Theory]
    [InlineData("c", false)]
    [InlineData("ê", false)]
    [InlineData("ế", false)]          // a Vietnamese word must not turn into a strip of pictures
    [InlineData("7", false)]
    [InlineData("?", false)]
    [InlineData(" ", false)]
    [InlineData("a", false)]
    [InlineData(Smiley, true)]
    [InlineData(Party, true)]
    [InlineData(Heart, true)]
    [InlineData(Family, true)]
    [InlineData(Flag, true)]
    public void OnlyEmojiAreTurnedIntoPictures(string element, bool expected)
        => Assert.Equal(expected, ColorEmoji.IsEmoji(element));

    [Theory]
    [InlineData("", 0)]
    [InlineData("cà phê", 1)]
    [InlineData("cà phê 😀", 2)]
    [InlineData("😀 hi", 2)]
    [InlineData("a😀b🎉c", 5)]
    [InlineData("😀🎉", 1)]         // two faces in a row are one run of emoji, whatever the picture count below
    [InlineData("👨‍👩‍👧", 1)]     // and a joined family is one element, so one picture, not three
    public void ASentenceIsCutBetweenTheWordsAndTheFaces(string text, int expectedPieces)
        => Assert.Equal(expectedPieces, ColorEmoji.Split(text).Count());

    [Fact]
    public void EveryPieceTogetherIsTheSentenceAgain()
    {
        const string text = "chúc mừng năm mới 2026 😀🎉 👨‍👩‍👧 hết";

        Assert.Equal(text, string.Concat(ColorEmoji.Split(text).Select(p => p.Piece)));
    }

    [Fact]
    public void TwoFacesInARowAreTwoPicturesNotOneSquashedOne()
    {
        // The cell is square because an emoji is square, so a run of two drawn into one cell would put the second
        // face outside the bitmap: it would be typed, counted, sent - and never seen.
        var rtf = ColorEmoji.InlineRtf(Smiley + Party, 24);

        Assert.NotNull(rtf);
        Assert.Equal(2, Count(rtf!, @"\pict"));
    }

    // ------------------------------------------------------------------ in the window

    [Fact]
    public void AFaceInAMessageArrivesInTheLineAsAPicture()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "hi " + Smiley));

            var rtb = Rt(form);
            var picture = Assert.Single(EmbeddedPictures(rtb));

            // The height of the line it sits on, not a thumbnail: a face that towers over a sentence moves the
            // sentence, and a face smaller than the words cannot be read.
            Assert.Equal(Math.Max(18, rtb.Font.Height), picture.Height);
            Assert.Equal(picture.Height, picture.Width);
        });
    }

    [Fact]
    public void TheWordsAreStillThereAndSoIsTheFaceOnlyNowItIsAPicture()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var typed = "cà phê " + Smiley;
            Box(form).Text = typed;
            Send(form);

            // The message that left this window is still exactly what was typed, faces and all: the picture is a
            // way of drawing, not a way of storing, and what the other machines receive is unchanged.
            var sent = Assert.Single(service.Transcript.OfType<TextMessage>(), m => m.Body.Contains(Smiley, StringComparison.Ordinal));
            Assert.Equal(typed, sent.Body);

            // And this is the cost of colour, written down as an assertion so that nobody discovers it later:
            // the words are still the control's text, the face is not.
            Assert.Contains("cà phê ", Transcript(form), StringComparison.Ordinal);
            Assert.DoesNotContain(Smiley, Transcript(form), StringComparison.Ordinal);
            Assert.Single(EmbeddedPictures(Rt(form)));

            // The box is where the picker puts a face, and it is still text there: a classic edit control cannot
            // contain an image at all. Colour in the box would mean a different control, which is its own decision.
            Assert.Equal(string.Empty, Box(form).Text);
        });
    }

    [Fact]
    public void AFaceInAnicknameIsStillWrittenAsText()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new TextMessage("User 2" + Smiley, "Room 1", "hello"));

            // Only the content of an entry is drawn in pictures. A headline made of pictures would turn every line
            // in the room into a mosaic for the sake of a name, and nobody asked for that.
            Assert.Contains("User 2" + Smiley, Transcript(form), StringComparison.Ordinal);
            Assert.Empty(EmbeddedPictures(Rt(form)));
        });
    }

    [Fact]
    public void APagesIconAndAFaceInTheSameRoomAreStillDifferentPictures()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            service.SimulateIncomingFile("slides.pdf", 4096, from: "User 2");
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "look " + Smiley));

            var sizes = EmbeddedPictures(Rt(form));

            Assert.Equal(2, sizes.Count);
            Assert.Contains(sizes, s => s.Width == FilePlaceholder.Width && s.Height == FilePlaceholder.Height);
            Assert.Contains(sizes, s => s.Width == s.Height && s.Width != FilePlaceholder.Width);
        });
    }

    // ------------------------------------------------------------------ helpers

    private static RichTextBox Rt(Control form) => (RichTextBox)form.Controls.Find("rtbChat", true)[0];

    private static ListBox Rooms(Control form) => (ListBox)form.Controls.Find("lstRooms", true)[0];

    private static string Transcript(Control form) => Rt(form).Text;

    private static TextBox Box(Control form) => (TextBox)form.Controls.Find("txtMessage", true)[0];

    private static void Send(Control form) => ((Button)form.Controls.Find("btnSend", true)[0]!).PerformClick();

    private static int Count(string haystack, string needle)
    {
        var count = 0;

        for (var at = haystack.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = haystack.IndexOf(needle, at + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>Pixels that are a colour rather than a shade of grey or of black ink.</summary>
    private static int ColouredPixels(Bitmap bitmap, int threshold)
    {
        var count = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                var max = Math.Max(p.R, Math.Max(p.G, p.B));
                var min = Math.Min(p.R, Math.Min(p.G, p.B));

                if (p.A > 0 && max - min > threshold)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int Shades(Bitmap bitmap)
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

    /// <summary>
    /// The pictures embedded in the transcript, decoded out of the RTF the control writes back out, as sizes.
    /// </summary>
    /// <remarks>
    /// Sizes and not bytes, for the reason found in v1.2: a RichTextBox re-encodes a picture when asked for its
    /// RTF, so the bytes that went in do not come back. A face inserted at the height of the line is unmistakable
    /// at that size, and a 40 × 46 is a page drawn in code.
    /// </remarks>
    private static List<(int Width, int Height)> EmbeddedPictures(RichTextBox rtb)
    {
        var found = new List<(int, int)>();

        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
            rtb.Rtf ?? string.Empty, @"89504e47[0-9a-fA-F\s]{200,}"))
        {
            var hex = new string(match.Value.Where(Uri.IsHexDigit).ToArray());

            using var png = new MemoryStream(Convert.FromHexString(hex));
            using var image = Image.FromStream(png);
            found.Add((image.Width, image.Height));
        }

        return found;
    }
}
