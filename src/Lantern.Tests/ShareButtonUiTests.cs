using Lantern.Core;
using Lantern.Domain;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T5.1: the Share button (F7, F8). One button, because a person with something to send should not have to
/// answer a second question about what kind of thing it is - so everything here is about the fork that
/// decision makes, and about the times the button must not send anything at all: nothing chosen, no room
/// entered, a photo too big for one frame, and a send already on its way.
/// </summary>
public sealed class ShareButtonUiTests
{
    [Fact]
    public void SharingAPictureSendsTheBytesItReads()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new SharingForm(service);
            var chat = (RichTextBox)Find(form, "rtbChat")!;
            var file = Write("sunset.png", Png(6, 3));

            try
            {
                form.Show();
                ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;
                form.Answer = file;

                ClickShare(form);

                var sent = Assert.Single(RoomTranscript(service).OfType<PictureMessage>());
                Assert.Equal("sunset.png", sent.FileName);
                Assert.Equal(File.ReadAllBytes(file), sent.Data);

                // The sender sees their own photo, because nothing is echoed back and the window is what
                // puts it on the screen.
                Assert.Contains("Haitao: [image] sunset.png", chat.Text, StringComparison.Ordinal);
                Assert.Contains("sunset.png", form.StatusText, StringComparison.Ordinal);
            }
            finally
            {
                Delete(file);
            }
        });
    }

    [Fact]
    public void AnythingElseIsSentAsAFile()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new SharingForm(service);
            var file = Write("lecture-slides.pdf", "not a picture".Length);

            try
            {
                form.Show();
                ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;
                form.Answer = file;

                ClickShare(form);

                var sent = Assert.Single(RoomTranscript(service).OfType<FileMessage>());
                Assert.Equal("lecture-slides.pdf", sent.FileName);
                Assert.Equal(new FileInfo(file).Length, sent.SizeBytes);

                // A file announcement says what it is; the bytes are chunks, not an inline image, so the
                // transcript must not sprout a thumbnail for a PDF.
                Assert.Empty(RoomTranscript(service).OfType<PictureMessage>());
                Assert.DoesNotContain(@"\pict", ((RichTextBox)Find(form, "rtbChat")!).Rtf ?? string.Empty, StringComparison.Ordinal);
            }
            finally
            {
                Delete(file);
            }
        });
    }

    [Fact]
    public void APictureTooBigForOneFrameIsRefusedBeforeAnythingIsSent()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new SharingForm(service);

            // Five and a bit megabytes, which is one byte past what the domain allows in a frame.
            var file = Write("huge.png", (int)ValidationRules.MaxPictureBytes + 1);

            try
            {
                form.Show();
                ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;
                form.Answer = file;

                ClickShare(form);

                // Told in the window's own status bar, in the domain's words, with nothing written to the
                // wire: the host would refuse it too, but only after the bytes had been read and sent.
                Assert.Contains("5 MB", form.StatusText, StringComparison.Ordinal);
                Assert.Empty(RoomTranscript(service).OfType<PictureMessage>());
            }
            finally
            {
                Delete(file);
            }
        });
    }

    [Fact]
    public void NothingChosenSendsNothingAndNeedsNoComment()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new SharingForm(service);

            form.Show();
            ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;
            var status = form.StatusText;
            form.Answer = null;

            ClickShare(form);

            Assert.Equal(1, form.PickerOpens);
            Assert.Empty(RoomTranscript(service).OfType<PictureMessage>());
            Assert.Empty(RoomTranscript(service).OfType<FileMessage>());
            Assert.Equal(status, form.StatusText);
        });
    }

    [Fact]
    public void SharingBeforeEnteringARoomSaysSoWithoutOpeningAFileDialog()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new SharingForm(service);
            var share = (Button)Find(form, "btnShare")!;

            form.Show();

            // F3: with nowhere to send it, the button is not offered at all - which is kinder than letting
            // somebody find a photo, press Open and only then be told to choose a room. Either way the
            // dialog must not have been opened.
            Assert.False(share.Enabled);

            share.PerformClick();

            Assert.Equal(0, form.PickerOpens);
            Assert.Empty(RoomTranscript(service).OfType<PictureMessage>());

            // And once a room is entered the same button works, so it was the room that was missing rather
            // than the button having never been wired up.
            ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;
            Assert.True(share.Enabled);
        });
    }

    [Fact]
    public void AFileThatVanishedBetweenTheDialogAndTheSendIsSaidNotThrown()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new SharingForm(service);

            form.Show();
            ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;
            form.Answer = Path.Combine(Path.GetTempPath(), "lantern-gone-" + Guid.NewGuid().ToString("N") + ".png");

            ClickShare(form);

            Assert.Contains("could not be read", form.StatusText, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(RoomTranscript(service).OfType<PictureMessage>());
        });
    }

    [Fact]
    public void SendAndShareAreGreyedOutWhileAShareIsOnItsWay()
    {
        Sta.Run(() =>
        {
            var service = new HangingSend();
            using var form = new SharingForm(service);
            var send = (Button)Find(form, "btnSend")!;
            var share = (Button)Find(form, "btnShare")!;
            var file = Write("in-flight.png", Png(4, 4));

            try
            {
                form.Show();
                ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;
                form.Answer = file;

                share.PerformClick();

                // F3: one thing at a time. Two pictures sent at once would arrive in whichever order the
                // socket felt like, and a second click while the first is going is how that happens.
                Assert.False(share.Enabled);
                Assert.False(send.Enabled);

                service.Release();
                Pump();

                Assert.True(share.Enabled);
                Assert.True(send.Enabled);
            }
            finally
            {
                service.Release();
                Pump();
                Delete(file);
            }
        });
    }

    [Fact]
    public void TheForkIsByNameAndItIsNotFussyAboutCase()
    {
        Assert.True(Attachments.IsPicture("sunset.png"));
        Assert.True(Attachments.IsPicture(@"C:\Users\haitao\PICTURES\SUNSET.JPG"));
        Assert.True(Attachments.IsPicture("scan.jpeg"));
        Assert.True(Attachments.IsPicture("diagram.gif"));

        // Not pictures: no extension at all, an unknown one, and the last extension of a name with several.
        Assert.False(Attachments.IsPicture("IMG_2048"));
        Assert.False(Attachments.IsPicture("notes.txt"));
        Assert.False(Attachments.IsPicture("archive.tar.gz"));
        Assert.False(Attachments.IsPicture(null));
        Assert.False(Attachments.IsPicture(string.Empty));

        // The dialog offers the same list the fork uses, so a picture cannot be hidden behind "All files".
        foreach (var extension in Attachments.PictureExtensions)
        {
            Assert.Contains("*" + extension, Attachments.PictureFilter, StringComparison.Ordinal);
            Assert.True(Attachments.IsPicture("x" + extension), $"{extension} is offered and then refused");
        }
    }

    // ---------------------------------------------------------------- plumbing

    /// <summary>A form that answers the file question instead of opening a dialog.</summary>
    private sealed class SharingForm : Lantern.UI.HostForm
    {
        public SharingForm(IChatService service) : base(service)
        {
        }

        // Fields rather than properties: WinForms serialises the public properties of a form, and a test
        // double for a person's choice has no business being written into a designer file.
        public string? Answer;

        public int PickerOpens;

        protected override string? AskForAttachment()
        {
            PickerOpens++;
            return Answer;
        }
    }

    /// <summary>A fake whose sends do not finish, so "in flight" can be looked at.</summary>
    private sealed class HangingSend : FakeChatService
    {
        public HangingSend() : base("Haitao")
        {
        }

        private readonly TaskCompletionSource<bool> released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task SendAsync(Message message, CancellationToken cancel = default) => released.Task;

        public void Release() => released.TrySetResult(true);
    }

    private static void ClickShare(Control form)
    {
        var share = (Button)Find(form, "btnShare")!;

        // A button only raises its click when it could really be clicked, so these tests need a window;
        // see ChatFormTests for the same note.
        share.PerformClick();
    }

    /// <summary>Let queued continuations run, which is what a window does when nobody is holding the thread.</summary>
    private static void Pump()
    {
        for (var i = 0; i < 40; i++)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }

    private static IEnumerable<Message> RoomTranscript(FakeChatService service)
        => service.Rooms[0].Transcript;

    /// <summary>
    /// A real file with a real name - which matters here more than usual, because the name is what decides
    /// picture or file and what ends up inside the message, so it cannot be padded with a unique prefix the
    /// way these tests normally do. Uniqueness comes from the folder instead.
    /// </summary>
    private static string Write(string name, byte[] bytes)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lantern-t51-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void Delete(string path)
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
        catch (IOException)
        {
            // Leftover files in the temp folder are untidy, not a failure.
        }
    }

    private static string Write(string name, int size) => Write(name, new byte[size]);

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>Looks a control up by the name the designer gave it, so the test says which one it meant.</summary>
    private static Control Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault()
            ?? throw new InvalidOperationException($"no control named {name}");

}
