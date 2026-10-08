using Lantern.Core;
using Lantern.Domain;
using Lantern.Storage;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T5.2 on the receiving side (F7): a picture in the transcript. A RichTextBox has no "add image" method,
/// so the thumbnail reaches it as RTF - which means the interesting questions are what that RTF has to
/// contain to be drawn at the right size, what happens to the line when the bytes turn out not to be a
/// picture, and what a double click on a caption is allowed to do with a file name that came from somebody
/// else's machine.
/// </summary>
public sealed class PictureTranscriptUiTests
{
    [Fact]
    public void AThumbnailBecomesRtfThatCarriesItsOwnSize()
    {
        using var bitmap = new Bitmap(4, 2);

        var rtf = RtfPicture.Inline(bitmap);

        Assert.NotNull(rtf);

        // Rich edit only draws a picture it is handed inside a \pict group, and it draws it at the size
        // the group states in twips - 15 twips to a pixel at 96 dpi. Get either wrong and the thumbnail is
        // either invisible or a four pixel speck, which no test can see but is what these lines are for.
        Assert.Contains(@"{\pict\pngblip", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\picw4\pich2", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\picwgoal60\pichgoal30", rtf, StringComparison.Ordinal);

        // The bytes in the group are a PNG whatever format the sender used, and 89 50 4E 47 is what a PNG
        // starts with - written as hex, because RTF is a text format.
        Assert.Contains("89504e47", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void BytesThatAreNotAPictureProduceNoRtf()
    {
        // Returning null is what lets the transcript fall back to the caption line. Throwing here would
        // cost the window the line, and every line after it.
        var message = new PictureMessage("User 2", "Room 1", "notes.txt", System.Text.Encoding.UTF8.GetBytes("a shopping list"));

        Assert.Null(RtfPicture.InlineFor(message));
    }

    [Fact]
    public void APictureIsDrawnUnderTheLineThatNamesIt()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);

            // A RichTextBox answers questions about its lines and its RTF by asking its window, so these
            // tests need one - unlike the tests that only ever read the cached Text.
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3)));

            Assert.Contains("User 2: [image] sunset.png", chat.Text, StringComparison.Ordinal);

            // The caption alone would satisfy F7 on paper and fail it in practice: the picture has to be
            // in the same window, above the fold of scrolling, without anybody opening anything.
            Assert.Contains(@"\pict", chat.Rtf, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void APictureThatWillNotDecodeStillLeavesItsLineInTheTranscript()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);

            // A RichTextBox answers questions about its lines and its RTF by asking its window, so these
            // tests need one - unlike the tests that only ever read the cached Text.
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "notes.txt", System.Text.Encoding.UTF8.GetBytes("not a picture")));

            Assert.Contains("User 2: [image] notes.txt", chat.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(@"\pict", chat.Rtf, StringComparison.Ordinal);

            // And the window is still a window afterwards: the next message arrives normally.
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "the part I typed"));
            Assert.Contains("the part I typed", chat.Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LeavingAndComingBackDrawsThePictureAgain()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);

            // A RichTextBox answers questions about its lines and its RTF by asking its window, so these
            // tests need one - unlike the tests that only ever read the cached Text.
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3)));

            rooms.SelectedIndex = 1;
            rooms.SelectedIndex = 0;

            // The transcript is rebuilt from the room's own history, so the thumbnail has to come out of
            // the message's bytes each time rather than from anything the window remembered.
            Assert.Contains("sunset.png", chat.Text, StringComparison.Ordinal);
            Assert.Contains(@"\pict", chat.Rtf, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DoubleClickingTheCaptionOfAPictureOpensThatPicture()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new OpeningForm(service);

            // A RichTextBox answers questions about its lines and its RTF by asking its window, so these
            // tests need one - unlike the tests that only ever read the cached Text.
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;

            var first = new PictureMessage("User 2", "Room 1", "one.png", Png(6, 3));
            var second = new PictureMessage("User 2", "Room 1", "two.png", Png(6, 3));
            service.SimulateIncoming(first);
            service.SimulateIncoming(second);

            var caption = chat.Lines.Single(line => line.Contains("two.png", StringComparison.Ordinal));
            form.OpenTranscriptLine(caption);

            // The picture the caption names, and not the first one that happens to be in the room.
            var opened = Assert.Single(form.Opened);
            Assert.Equal("two.png", opened.FileName);
            Assert.Equal(second.Id, opened.Id);

            // The caption of the other photo still opens its own picture, and a line that is not a picture
            // does nothing at all - no viewer, no error, no guessing about which message was meant.
            form.OpenTranscriptLine(chat.Lines.First(line => line.Contains("one.png", StringComparison.Ordinal)));
            Assert.Equal(2, form.Opened.Count);
            Assert.Equal("one.png", form.Opened[^1].FileName);

            // A line that is not a caption does nothing at all: no viewer, no error, no guessing about
            // which message the person meant. The transcript is matched by line, not by "nearest picture".
            form.OpenTranscriptLine("nothing like a transcript line");
            form.OpenTranscriptLine("two.png");
            form.OpenTranscriptLine(string.Empty);
            Assert.Equal(2, form.Opened.Count);
        });
    }

    [Fact]
    public void ADoubleClickUsesTheLineUnderTheCaret()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new OpeningForm(service);

            // A RichTextBox answers questions about its lines and its RTF by asking its window, so these
            // tests need one - unlike the tests that only ever read the cached Text.
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;
            var picture = new PictureMessage("User 2", "Room 1", "caret.png", Png(6, 3));
            service.SimulateIncoming(picture);

            var index = chat.Text.IndexOf("caret.png", StringComparison.Ordinal);
            Assert.True(index >= 0);

            chat.SelectionStart = index;
            form.OpenTranscriptLineAtCaret();

            var opened = Assert.Single(form.Opened);
            Assert.Equal(picture.Id, opened.Id);

            // The thumbnail occupies the line below the caption, so the caret can also be there - and a
            // caret in an empty window must not open the last picture seen.
            form.ClearOpened();
            chat.SelectionStart = 0;
            rooms.SelectedIndex = 1;
            form.OpenTranscriptLineAtCaret();
            Assert.Empty(form.Opened);
        });
    }

    [Fact]
    public void AFileNameFromAPeerNeverArrivesAsAPathOnThisMachine()
    {
        var id = Guid.NewGuid();

        // These names are what a transcript and a save suggestion are built from, so they are reduced to
        // one component before anything else is asked to touch the disk.
        Assert.Equal($"{id:N}-sam", ReceivedPayloads.FileNameFor(@"..\..\windows\system32\config\sam", id));
        Assert.Equal($"{id:N}-cat.png", ReceivedPayloads.FileNameFor(@"C:\Users\haitao\Pictures\cat.png", id));
        Assert.Equal($"{id:N}-photo.png", ReceivedPayloads.FileNameFor("photo.png", id));

        // Two photos both called photo.jpg from two different messages are two files.
        Assert.NotEqual(
            ReceivedPayloads.FileNameFor("photo.jpg", id),
            ReceivedPayloads.FileNameFor("photo.jpg", Guid.NewGuid()));

        // Characters Windows refuses in a name, and a name that is nothing but dots.
        var sanitized = ReceivedPayloads.FileNameFor(@"a<b>|c?.txt", id);
        Assert.StartsWith($"{id:N}-", sanitized, StringComparison.Ordinal);

        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            Assert.DoesNotContain(bad, sanitized[(id.ToString("N").Length + 1)..]);
        }

        Assert.EndsWith("-received", ReceivedPayloads.FileNameFor("...", id), StringComparison.Ordinal);
        Assert.EndsWith("-received", ReceivedPayloads.FileNameFor(null, id), StringComparison.Ordinal);

        // A name from somebody else's machine is not a licence to write a gigabyte-wide transcript line.
        Assert.Equal($"{id:N}-{new string('n', ReceivedPayloads.NameBudget)}.png",
            ReceivedPayloads.FileNameFor(new string('n', 400) + ".png", id));
    }

    [Fact]
    public void SavingTheSamePictureTwiceIsNotTwoWrites()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lantern-t52-" + Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid();
        var bytes = Png(6, 3);

        try
        {
            var path = ReceivedPayloads.Save("sunset.png", id, bytes, folder);
            Assert.True(File.Exists(path));
            Assert.Equal(bytes, File.ReadAllBytes(path));

            var written = File.GetLastWriteTimeUtc(path);
            Assert.Equal(path, ReceivedPayloads.Save("sunset.png", id, bytes, folder));
            Assert.Equal(written, File.GetLastWriteTimeUtc(path));

            // Somebody deleted it - a person clearing their Downloads folder while a photo is still in the
            // transcript - so it is written again rather than reported as missing.
            File.Delete(path);
            Assert.Equal(path, ReceivedPayloads.Save("sunset.png", id, bytes, folder));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void OpeningSomethingThatIsNotThereIsAnsweredWithNo()
    {
        Assert.False(ReceivedPayloads.TryOpen(Path.Combine(Path.GetTempPath(), "lantern-no-such-file-" + Guid.NewGuid().ToString("N") + ".png")));

        // Where the copies live is in the user's own area, not beside the program and not in the current
        // directory, which is wherever the shortcut happened to say.
        Assert.EndsWith(Path.Combine("Lantern", "cache"), ReceivedPayloads.CacheDirectory, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>A form that records what a double click asked for instead of launching the photo viewer.</summary>
    private sealed class OpeningForm : Lantern.UI.HostForm
    {
        /// <summary>A field, not a property: WinForms serialises public properties of a form.</summary>
        public readonly List<PictureMessage> Opened = [];

        public OpeningForm(IChatService service) : base(service)
        {
        }

        public void ClearOpened() => Opened.Clear();

        protected override void OpenPicture(PictureMessage picture) => Opened.Add(picture);
    }

    private static Control? Find(Control parent, string name)
    {
        foreach (var control in parent.Controls)
        {
            if (control is not Control child)
            {
                continue;
            }

            if (child.Name == name)
            {
                return child;
            }

            if (Find(child, name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// A PNG of the given size. Any size will do for these tests; the pixel count is what the thumbnail
    /// maths and the RTF group are about.
    /// </summary>
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
}
