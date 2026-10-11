using Lantern.Core;
using Lantern.Domain;
using Lantern.Storage;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// F14: a picture can be looked at. Double-clicking a photo in the transcript opens it in a window of its own,
/// larger than the transcript could ever be, and the person looking decides how large - because a 160 pixel
/// thumbnail answers "was a photo sent?" and nothing else.
/// <para>
/// The two things most likely to go wrong here are not the scaling maths, which is four lines, but the file
/// that a window like this used to write without being asked, and the window that never goes away. Both are
/// asserted below.
/// </para>
/// </summary>
public class PictureViewerTests
{
    [Fact]
    public void APictureWindowShowsThePictureItWasGiven()
    {
        Sta.Run(() =>
        {
            var picture = new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3));
            using var viewer = new PictureViewerForm(picture);

            Assert.Equal("sunset.png", viewer.Text);
            Assert.Equal(picture.Id, viewer.Picture.Id);
            Assert.True(viewer.HasPicture);

            // Who sent it, when, and how big it really is - the three questions asked the moment a photo opens,
            // answered without going back to the transcript to look up the line it came from.
            Assert.Contains("User 2", viewer.StatusText, StringComparison.Ordinal);
            Assert.Contains("6 × 3", viewer.StatusText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void APictureWindowOpensLargeEnoughToRead()
    {
        Sta.Run(() =>
        {
            using var viewer = new PictureViewerForm(new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3)));
            viewer.Show();

            var working = Screen.PrimaryScreen!.WorkingArea;

            // A window whose entire job is showing a photo bigger than a thumbnail must not open the size of a
            // thumbnail. Measured against the screen rather than a fixed number, because "large" is a fraction
            // of what there is.
            Assert.True(
                viewer.Width >= working.Width * 0.5 && viewer.Height >= working.Height * 0.5,
                $"opened at {viewer.Width}×{viewer.Height} on a {working.Width}×{working.Height} screen");
        });
    }

    [Fact]
    public void AWindowOpensFittedAndNeverEnlargesAPhoto()
    {
        Sta.Run(() =>
        {
            using var viewer = new PictureViewerForm(new PictureMessage("User 2", "Room 1", "tiny.png", Png(6, 3)));
            viewer.Show();

            Assert.True(viewer.IsFitted);

            // Six pixels shown at six pixels. Stretching a photo to fill the window would show the same detail
            // made of four times as many invented pixels, and the person who wants that has a button for it.
            Assert.Equal(1.0, viewer.Zoom);
        });
    }

    [Fact]
    public void ZoomingStepsAndStopsAtBothEnds()
    {
        Sta.Run(() =>
        {
            using var viewer = new PictureViewerForm(new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3)));

            viewer.ActualSize();
            Assert.Equal(1.0, viewer.Zoom);

            viewer.ZoomIn();
            Assert.Equal(PictureViewerForm.ZoomStep, viewer.Zoom, 3);

            viewer.ZoomOut();
            Assert.Equal(1.0, viewer.Zoom, 3);

            viewer.ZoomOut();
            Assert.Equal(1.0 / PictureViewerForm.ZoomStep, viewer.Zoom, 3);

            // Held the wheel down for a minute in either direction, the window ends up at the end of its range
            // rather than at zero, or at a size no monitor has.
            for (var i = 0; i < 200; i++)
            {
                viewer.ZoomOut();
            }

            Assert.Equal(PictureViewerForm.MinimumZoom, viewer.Zoom, 3);

            for (var i = 0; i < 400; i++)
            {
                viewer.ZoomIn();
            }

            Assert.Equal(PictureViewerForm.MaximumZoom, viewer.Zoom, 3);
        });
    }

    [Fact]
    public void TheScaleIsSaidOutLoud()
    {
        Sta.Run(() =>
        {
            using var viewer = new PictureViewerForm(new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3)));
            viewer.Show();

            viewer.ActualSize();
            Assert.Equal("100%", viewer.ZoomText);

            viewer.ZoomIn();
            Assert.Equal("125%", viewer.ZoomText);
        });
    }

    [Fact]
    public void FitAndActualSizeAreDifferentThingsAndDoubleClickingTogglesBetweenThem()
    {
        Sta.Run(() =>
        {
            using var viewer = new PictureViewerForm(new PictureMessage("User 2", "Room 1", "sunset.png", Png(600, 400)));
            viewer.Show();

            Assert.True(viewer.IsFitted);

            viewer.ToggleFit();
            Assert.False(viewer.IsFitted);
            Assert.Equal(1.0, viewer.Zoom, 3);

            viewer.ToggleFit();
            Assert.True(viewer.IsFitted);
        });
    }

    [Fact]
    public void ThePictureIsActuallyDrawnAtTheChosenScale()
    {
        Sta.Run(() =>
        {
            using var viewer = new PictureViewerForm(new PictureMessage("User 2", "Room 1", "sunset.png", Png(60, 40)));
            viewer.Show();

            var picture = (PictureBox)Find(viewer, "picImage")!;

            viewer.ActualSize();
            Assert.Equal(60, picture.Width);
            Assert.Equal(40, picture.Height);

            viewer.ZoomIn();
            Assert.Equal(75, picture.Width);
            Assert.Equal(50, picture.Height);

            // The scale has to reach the control that paints, or the label tells the truth about a window that
            // is still showing the old size.
            Assert.Equal((int)Math.Round(60 * viewer.Zoom), picture.Width);
        });
    }

    [Fact]
    public void APhotoBiggerThanTheWindowIsShownWholeAndScrollable()
    {
        Sta.Run(() =>
        {
            // "Bigger than the window" is the property, and the window is the thing this test controls. It used
            // to be written the other way round - a 4000 × 3000 photo, hoping no screen was that wide - which
            // allocated 48 MB of GDI+ and was refused partway through a run of five hundred other tests that had
            // each made a window. A test that passes alone and fails in a crowd gets deleted as flaky, so now the
            // window is made small and the photo merely large.
            using var viewer = new PictureViewerForm(new PictureMessage("User 2", "Room 1", "huge.png", Png(1400, 900)));
            viewer.Show();
            viewer.Width = 600;

            var panel = (Panel)Find(viewer, "pnlImage")!;
            var picture = (PictureBox)Find(viewer, "picImage")!;

            Assert.True(panel.AutoScroll, "a photo larger than the window needs the window to scroll");

            viewer.ActualSize();

            // One to one means the whole photo is there, the part outside the window included - not a crop of
            // the middle of it.
            Assert.Equal(1400, picture.Width);
            Assert.True(picture.Width > panel.ClientSize.Width);
        });
    }

    [Fact]
    public void TheScalingRulesAreNumbersAndNotWindows()
    {
        // Fitting a 2000 × 1000 photo into 800 × 600 is bound by the width.
        Assert.Equal(0.4, PictureViewerForm.FitScale(2000, 1000, 800, 600), 3);

        // A photo smaller than the window stays its own size.
        Assert.Equal(1.0, PictureViewerForm.FitScale(100, 100, 800, 600), 3);

        // Nothing to fit into yet - a window being constructed has no size, and inventing a scale from a
        // division by zero is how a photo opens the size of a postage stamp.
        Assert.Equal(1.0, PictureViewerForm.FitScale(0, 0, 0, 0), 3);

        Assert.Equal(PictureViewerForm.MinimumZoom, PictureViewerForm.ClampZoom(0.0001), 3);
        Assert.Equal(PictureViewerForm.MaximumZoom, PictureViewerForm.ClampZoom(10_000), 3);
        Assert.Equal(1.0, PictureViewerForm.ClampZoom(double.NaN), 3);
        Assert.Equal(2.5, PictureViewerForm.ClampZoom(2.5), 3);
    }

    [Fact]
    public void BytesThatAreNotAPictureStillOpenAWindowThatSaysSo()
    {
        Sta.Run(() =>
        {
            var notAPicture = new PictureMessage("User 2", "Room 1", "notes.txt", System.Text.Encoding.UTF8.GetBytes("a shopping list"));
            using var viewer = new PictureViewerForm(notAPicture);
            viewer.Show();

            // The person who double-clicked gets an answer - "that is not a picture" - rather than a window
            // that threw, a window that is empty and silent, or no window at all.
            Assert.False(viewer.HasPicture);
            Assert.Contains("did not arrive as a picture", viewer.StatusText, StringComparison.Ordinal);
            Assert.Equal("—", viewer.ZoomText);

            // Nothing to scale, so nothing to scale it with: the buttons are greyed rather than there to be
            // clicked and have nothing happen.
            Assert.False(Enabled(viewer, "btnZoomIn"));
            Assert.False(Enabled(viewer, "btnFit"));

            // And the window is still a window: it closes, and nothing about the rest of the chat changed.
            Assert.Equal("notes.txt", viewer.Text);
        });
    }

    [Fact]
    public void DoubleClickingAPictureInTheTranscriptOpensItsOwnWindow()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new ClientForm(service);
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;

            var picture = new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3));
            service.SimulateIncoming(picture);

            Assert.True(form.TryOpenPictureAt(chat.Text.IndexOf("sunset.png", StringComparison.Ordinal)));

            var opened = Assert.Single(form.OpenPictures);

            Assert.Equal(picture.Id, opened.Picture.Id);
            Assert.True(opened.Visible);
            Assert.Contains("showing sunset.png", form.StatusText, StringComparison.Ordinal);

            // Owned by the chat window rather than floating free of it, and not modal: the room keeps talking
            // while the photo is up.
            Assert.Same(form, opened.Owner);
        });
    }

    [Fact]
    public void TheSamePhotoTwiceIsOneWindowAndTwoPhotosAreTwo()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new ClientForm(service);
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "one.png", Png(6, 3)));
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "two.png", Png(6, 3)));

            for (var i = 0; i < 3; i++)
            {
                form.TryOpenPictureAt(chat.Text.IndexOf("one.png", StringComparison.Ordinal));
            }

            var first = Assert.Single(form.OpenPictures);

            form.TryOpenPictureAt(chat.Text.IndexOf("two.png", StringComparison.Ordinal));
            Assert.Equal(2, form.OpenPictures.Count);

            // Closing one of them leaves the other, and the photo that went away can be opened again - a window
            // that stayed on the list after it closed could never be raised again.
            var other = form.OpenPictures.Single(v => v != first);
            other.Close();

            Assert.Single(form.OpenPictures);

            form.TryOpenPictureAt(chat.Text.IndexOf("two.png", StringComparison.Ordinal));
            Assert.Equal(2, form.OpenPictures.Count);
        });
    }

    [Fact]
    public void ClosingTheChatWindowClosesThePhotosItOpened()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            var form = new ClientForm(service);
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3)));
            form.TryOpenPictureAt(chat.Text.IndexOf("sunset.png", StringComparison.Ordinal));

            var viewer = Assert.Single(form.OpenPictures);

            form.Close();

            // A photo left on screen after the room that carried it was closed is a window out of a chat that
            // no longer exists.
            Assert.True(viewer.IsDisposed);
            Assert.Empty(form.OpenPictures);
        });
    }

    [Fact]
    public void LookingAtAPictureWritesNoFileAnywhere()
    {
        Sta.Run(() =>
        {
            var before = FilesInCache();

            var service = new FakeChatService("Haitao");
            using var form = new ClientForm(service);
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3)));

            form.TryOpenPictureAt(chat.Text.IndexOf("sunset.png", StringComparison.Ordinal));

            foreach (var viewer in form.OpenPictures.ToArray())
            {
                viewer.Close();
            }

            // The bytes are already in the message. Looking at a photo used to also put a copy of it in a
            // folder in app data and hand it to the system's photo viewer - a file nobody asked for, in a
            // window that is not this program's, on the path of a double click.
            Assert.Equal(before, FilesInCache());
        });
    }

    [Fact]
    public void SavingAPictureWritesThoseBytesWhereThePersonSaid()
    {
        Sta.Run(() =>
        {
            var destination = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-kept.png");
            var picture = new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3));

            try
            {
                using var viewer = AnswerableViewer.With(destination, picture);
                viewer.Show();

                Click(viewer, "btnSave");

                Assert.True(File.Exists(destination));
                Assert.Equal(picture.Data, File.ReadAllBytes(destination));
                Assert.Contains("saved to", viewer.StatusText, StringComparison.Ordinal);
            }
            finally
            {
                File.Delete(destination);
            }
        });
    }

    [Fact]
    public void DecliningToSaveWritesNothing()
    {
        Sta.Run(() =>
        {
            var wouldHaveBeen = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-never.png");

            using var viewer = AnswerableViewer.With(null, new PictureMessage("User 2", "Room 1", "sunset.png", Png(6, 3)));
            viewer.Show();

            Click(viewer, "btnSave");

            Assert.False(File.Exists(wouldHaveBeen));
            Assert.Contains("kept it in the chat only", viewer.StatusText, StringComparison.Ordinal);
        });
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Everything in the folder received payloads used to be cached in, by name.</summary>
    private static string[] FilesInCache()
    {
        var directory = ReceivedPayloads.CacheDirectory;

        return Directory.Exists(directory)
            ? Directory.GetFiles(directory).OrderBy(path => path, StringComparer.Ordinal).ToArray()
            : [];
    }

    /// <summary>
    /// A viewer that answers the save question instead of a person, so that writing the file - which is what
    /// the button is for - can be tested at all. The asking is one method
    /// (<see cref="PictureViewerForm.AskForSavePath"/>) for exactly this reason.
    /// </summary>
    private sealed class AnswerableViewer : PictureViewerForm
    {
        private readonly string? answer;

        private AnswerableViewer(string? answer, PictureMessage picture)
            : base(picture) => this.answer = answer;

        public static AnswerableViewer With(string? answer, PictureMessage picture) => new(answer, picture);

        protected override string? AskForSavePath() => answer;
    }

    /// <summary>
    /// Clicks a button on the window's tool strip. A tool strip item is not a control, so
    /// <see cref="Control.Controls"/> does not hold it and its name has to be looked for among the items.
    /// </summary>
    private static void Click(Control root, string name)
    {
        var strips = root.Controls.Find("toolStrip", searchAllChildren: true).OfType<ToolStrip>();

        var button = strips
            .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .FirstOrDefault(item => item.Name == name);

        Assert.NotNull(button);
        button!.PerformClick();
    }

    /// <summary>Whether a tool strip button can be used, for the window that has no picture to scale.</summary>
    private static bool Enabled(Control root, string name)
    {
        var strips = root.Controls.Find("toolStrip", searchAllChildren: true).OfType<ToolStrip>();

        return strips
            .SelectMany(strip => strip.Items.OfType<ToolStripButton>())
            .Single(item => item.Name == name)
            .Enabled;
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    private static Control? Find(Control root, string name)
        => root.Controls.Find(name, searchAllChildren: true).FirstOrDefault();
}
