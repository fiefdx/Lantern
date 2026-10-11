using System.Reflection;
using System.Runtime.InteropServices;
using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// The small page a shared file gets beside its entry.
/// </summary>
/// <remarks>
/// Two things this is for. A file was the one entry in the transcript with nothing to look at - every picture had
/// a thumbnail - and, following from that, nothing to point at: "save that file" could only be asked of a sentence.
/// The page is drawn in code from the file's name, so it is measured here like any other colour and shape in the
/// window rather than trusted because a designer drew it once, somewhere.
/// </remarks>
public sealed class FilePlaceholderTests
{
    private const int WM_CONTEXTMENU = 0x007B;

    [DllImport("user32.dll")]
    private static extern IntPtr PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [Theory]
    [InlineData("deck.PDF", "PDF")]
    [InlineData("notes.txt", "TXT")]
    [InlineData("holiday.JPG", "JPG")]
    [InlineData("archive", "FILE")]           // no extension to read
    [InlineData("backup.", "FILE")]           // an extension dot and nothing after it
    [InlineData("thing.xlsx1234", "FILE")]    // long enough to be mud at this size
    [InlineData("weird.???", "FILE")]         // not a word, whatever it is
    [InlineData("", "FILE")]
    public void TheExtensionIsWhatIsWrittenOnThePage(string fileName, string expected)
        => Assert.Equal(expected, FilePlaceholder.Label(fileName));

    [Fact]
    public void ThePageIsDrawnRatherThanAssumed()
    {
        using var page = FilePlaceholder.Bitmap("deck.pdf");

        Assert.Equal(FilePlaceholder.Width, page.Width);
        Assert.Equal(FilePlaceholder.Height, page.Height);

        // Small enough to sit beside a line of chat: a 400 pixel icon in the transcript would push the
        // conversation off the screen, and the whole point is that the file and its message share a glance.
        Assert.True(page.Width <= 64 && page.Height <= 64, "a placeholder is a small picture, not a poster");

        var white = 0;
        var inked = 0;

        for (var y = 0; y < page.Height; y++)
        {
            for (var x = 0; x < page.Width; x++)
            {
                var pixel = page.GetPixel(x, y);
                if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240)
                {
                    white++;
                }
                else
                {
                    inked++;
                }
            }
        }

        // Both halves of the claim: something was drawn, and it is a page rather than a block.
        Assert.True(inked > 40, $"the page looks blank: only {inked} pixels are not white");
        Assert.True(white > inked, "a page is mostly paper");
    }

    [Theory]
    [InlineData(0x1F, 0x4E, 0x79)]
    [InlineData(0x6E, 0x6E, 0x6E)]
    public void EverythingDrawnOnItCanBeReadOnTheTranscript(byte r, byte g, byte b)
        => Assert.True(Contrast(Color.White, Color.FromArgb(r, g, b)) >= 4.5,
            $"{r:X2}{g:X2}{b:X2} on the transcript's white is {Contrast(Color.White, Color.FromArgb(r, g, b)):0.##}:1");

    [Fact]
    public void ASharedFileHasAPictureOfItsOwnInTheTranscript()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            Assert.Equal(0, EmbeddedPictures(Rt(form)));

            var job = service.SimulateIncomingFile("deck.pdf", 4_096);
            service.AdvanceFile(job, 4_096);

            Assert.Contains("[file] deck.pdf", Transcript(form), StringComparison.Ordinal);
            Assert.Equal(1, EmbeddedPictures(Rt(form)));

            // The page belongs to the entry: a caret on it is a caret on that file, which is what the next two
            // tests - a right click that offers the file, a double click that opens no viewer - depend on.
            Assert.True(form.CanSaveSharedAt(EntryPictureAt(Rt(form), "[file] deck.pdf")));
        });
    }

    [Fact]
    public void APictureEntryGetsItsThumbnailAndNoPage()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", OnePixelPng()));

            // One embedded picture: the photo's own thumbnail. A page beside a photo would be two things to
            // click on and one photo.
            Assert.Equal(1, EmbeddedPictures(Rt(form)));
        });
    }

    [Fact]
    public void ARightClickOnThePageOffersToSaveThatFile()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var job = service.SimulateIncomingFile("deck.pdf", 4_096);
            service.AdvanceFile(job, 4_096);

            var rtb = Rt(form);
            var menu = RightClickAt(form, EntryPictureAt(rtb, "[file] deck.pdf"));

            Assert.True(menu.Visible, "the page is something a right click can do something about");
            Assert.Equal("Save file to…", menu.Items[0].Text);

            menu.Close();
        });
    }

    [Fact]
    public void ADoubleClickOnThePageOpensNoViewer()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var job = service.SimulateIncomingFile("deck.pdf", 4_096);
            service.AdvanceFile(job, 4_096);
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", OnePixelPng()));

            // A file is not a picture and the viewer is for pictures. It has no bytes to show - they may still be
            // arriving, or may have been declined - so a viewer opened on a file would be an empty window again.
            DoubleClickAt(form, EntryPictureAt(Rt(form), "[file] deck.pdf"));

            Assert.Equal(0, form.OwnedForms.Count(f => f is PictureViewerForm));
        });
    }

    [Fact]
    public void ADoubleClickOnAThumbnailStillOpensTheViewer()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", OnePixelPng()));

            // And something after it, so the test is not relying on a thumbnail being the last thing in the
            // window: the first embedded picture is the photo's, and the page comes later.
            var job = service.SimulateIncomingFile("deck.pdf", 4_096);
            service.AdvanceFile(job, 4_096);
            Assert.Equal(2, EmbeddedPictures(Rt(form)));

            DoubleClickAt(form, EntryPictureAt(Rt(form), "[image] sunset.png"));

            var opened = form.OwnedForms.Count(f => f is PictureViewerForm);
            Assert.Equal(1, opened);

            form.OwnedForms.First(f => f is PictureViewerForm).Close();
        });
    }

    // ------------------------------------------------------------------ harness

    private static RichTextBox Rt(Control form) => (RichTextBox)form.Controls.Find("rtbChat", true)[0];

    private static ListBox Rooms(Control form) => (ListBox)form.Controls.Find("lstRooms", true)[0];

    private static string Transcript(Control form) => Rt(form).Text;

    /// <summary>
    /// How many pictures are embedded in the transcript - thumbnails and pages alike.
    /// <para>
    /// Counted in the RTF rather than in <c>Text</c>: a RichTextBox leaves an embedded picture out of its plain
    /// text altogether - the line it sits on reads as a single space - which is precisely why "is the picture
    /// actually in there" cannot be answered from the string the window shows.
    /// </para>
    /// </summary>
    private static int EmbeddedPictures(RichTextBox rtb)
        => System.Text.RegularExpressions.Regex.Matches(rtb.Rtf ?? string.Empty, @"\\pict").Count;

    /// <summary>The start of the line an entry's own picture is written on, given a word from that entry.</summary>
    private static int EntryPictureAt(RichTextBox rtb, string somethingInTheEntry)
    {
        var index = rtb.Text.IndexOf(somethingInTheEntry, StringComparison.Ordinal);
        Assert.True(index >= 0, $"there is no {somethingInTheEntry} in the transcript");

        var entryLine = rtb.GetLineFromCharIndex(index);
        var start = rtb.GetFirstCharIndexFromLine(entryLine + 1);

        Assert.True(start > index, $"nothing was written on the line below {somethingInTheEntry}");
        return start;
    }

    private static ContextMenuStrip RightClickAt(Control form, int caret)
    {
        var rtb = Rt(form);
        var onScreen = rtb.PointToScreen(rtb.GetPositionFromCharIndex(caret));

        Cursor.Position = onScreen;
        PostMessage(rtb.Handle, WM_CONTEXTMENU, rtb.Handle, (onScreen.Y << 16) | (onScreen.X & 0xFFFF));
        Pump();

        return rtb.ContextMenuStrip ?? throw new InvalidOperationException("the transcript has no menu attached");
    }

    /// <summary>Raises the transcript's double click the way the framework does; see PictureOnScreenTests.</summary>
    private static void DoubleClickAt(Control form, int caret)
    {
        var rtb = Rt(form);
        var where = rtb.GetPositionFromCharIndex(caret);

        var raise = typeof(Control).GetMethod("OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(raise);

        raise!.Invoke(rtb, new object?[] { new MouseEventArgs(MouseButtons.Left, 2, where.X, where.Y, 0) });
        Pump();
    }

    private static void Pump()
    {
        for (var i = 0; i < 10; i++)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg==");

    private static double Contrast(Color first, Color second)
    {
        var one = Luminance(first);
        var two = Luminance(second);
        var lighter = Math.Max(one, two);
        var darker = Math.Min(one, two);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(int value)
        {
            var s = value / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }
}
