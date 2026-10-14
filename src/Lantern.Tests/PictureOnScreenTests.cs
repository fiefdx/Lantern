using System.Reflection;
using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// The photo, on the screen.
/// </summary>
/// <remarks>
/// Nothing else in the suite asked for this. The viewer's own tests read the scale out of a label and the size
/// out of a control; the transcript's tests record which picture was asked for through a subclass whose
/// <c>OpenPicture</c> opens no window at all. Between them they covered a viewer that had never once displayed a
/// picture - it fitted, sized, zoomed and reported a percentage over an empty panel, in every version ever
/// committed, because <c>picImage.Image</c> did not appear anywhere in the program.
/// <para>
/// This is the third defect to live in the same gap - a dialog's OK button no test had clicked, a context menu no
/// test had opened, and now a photo no test had looked at - and the first that a suite-wide green run hid for two
/// milestones. A seam that performs the behaviour under test cannot probe it, and a feature the suite cannot
/// perceive has to be measured in pixels or not claimed at all.
/// </para>
/// </remarks>
public sealed class PictureOnScreenTests
{
    [Fact]
    public void ThePictureIsGivenToTheControlThatPaintsIt()
    {
        Sta.Run(() =>
        {
            using var viewer = new PictureViewerForm(
                new PictureMessage("User 2", "Room 1", "sunset.png", Red(120, 80)));

            viewer.Show();

            var pic = (PictureBox)Find(viewer, "picImage")!;

            // The whole defect, in one line: the window had the picture and the control that paints did not.
            Assert.NotNull(pic.Image);
            Assert.Equal(120, pic.Image!.Width);
            Assert.Equal(80, pic.Image.Height);
        });
    }

    [Fact]
    public void LookingAtTheWindowShowsThePhotoRatherThanAnEmptyPanel()
    {
        Sta.Run(() =>
        {
            using var viewer = new PictureViewerForm(
                new PictureMessage("User 2", "Room 1", "sunset.png", Red(120, 80)));

            viewer.Show();

            var pic = (PictureBox)Find(viewer, "picImage")!;
            var panel = (Panel)Find(viewer, "pnlImage")!;

            // Draw the window and look at the middle of where the photo should be. The panel behind it is a
            // deliberate dark grey, so "is that the photo" is a question a pixel answers - and nothing said about
            // a label, a size or a percentage can answer it.
            using var drawn = new Bitmap(viewer.ClientSize.Width, viewer.ClientSize.Height);
            viewer.DrawToBitmap(drawn, new Rectangle(0, 0, drawn.Width, drawn.Height));

            var scroll = panel.AutoScrollPosition;
            var x = panel.Left + scroll.X + pic.Left + (pic.Width / 2);
            var y = panel.Top + scroll.Y + pic.Top + (pic.Height / 2);
            var seen = drawn.GetPixel(x, y);

            Assert.True(seen.R > 180 && seen.G < 80 && seen.B < 80,
                $"the middle of the photo should be the red that was sent; it was #{seen.R:X2}{seen.G:X2}{seen.B:X2}");
        });
    }

    [Fact]
    public void ADoubleClickInTheTranscriptOpensAWindowWithThePhotoInIt()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", Red(140, 90)));

            DoubleClickTranscriptAt(form, "sunset.png");

            // Not OpenPicture, which the other tests override and record: an actual window, owned by this one,
            // with an actual picture on it.
            var opened = form.OwnedForms.Count(f => f is PictureViewerForm);
            Assert.Equal(1, opened);

            var viewer = form.OwnedForms.First(f => f is PictureViewerForm);
            var pic = (PictureBox)viewer.Controls.Find("picImage", true)[0];

            Assert.NotNull(pic.Image);
            Assert.Equal(140, pic.Image!.Width);

            viewer.Close();
        });
    }

    [Fact]
    public void ADoubleClickOnALineThatIsNotAPictureOpensNothing()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", Red(140, 90)));
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "a line in between"));

            DoubleClickTranscriptAt(form, "a line in between");

            Assert.Equal(0, form.OwnedForms.Count(f => f is PictureViewerForm));
        });
    }

    /// <summary>
    /// The middle of a thumbnail, aimed at in pixels rather than at a character.
    /// </summary>
    /// <remarks>
    /// Every other double-click test here aims at an entry's WORDS, which is a line of text any hit rule can find.
    /// A thumbnail is a picture, and a click below or to the right of its top-left pixel lands on the character
    /// AFTER it. Aiming at the picture's own character tests the corner of the photo and nothing else, which is how
    /// the right-click menu came to be tested against a page nobody could hit in the middle (v1.3.1).
    /// </remarks>
    [Fact]
    public void ADoubleClickInTheMiddleOfAThumbnailOpensTheViewer()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", Red(140, 90)));

            var rtb = (RichTextBox)Find(form, "rtbChat")!;
            var words = rtb.Text.IndexOf("sunset.png", StringComparison.Ordinal);

            // The photo's own line begins below its caption, and it is shown at 140 × 90: fitting never enlarges.
            var corner = rtb.GetPositionFromCharIndex(rtb.GetFirstCharIndexFromLine(rtb.GetLineFromCharIndex(words) + 1));

            var raise = typeof(Control).GetMethod(
                "OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(raise);

            raise!.Invoke(rtb, new object?[]
            {
                new MouseEventArgs(MouseButtons.Left, 2, corner.X + 70, corner.Y + 45, 0),
            });

            for (var i = 0; i < 8; i++)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }

            var opened = form.OwnedForms.OfType<PictureViewerForm>().ToList();

            Assert.Single(opened);

            opened[0].Close();
        });
    }

    [Fact]
    public void ADoubleClickOnTheFarSideOfAThumbnailOpensTheViewer()
    {
        // "not every time I double click a image shared in the transcript can open the image viewer window,
        // sometimes there is no reaction at all." There is a line inside a photograph where the control stops
        // answering for it. Measured on a 140 x 90 thumbnail: every click in its left 100 pixels reports the
        // picture's own character, and every click in the right third reports the character AFTER it - which the
        // rule for opening a picture can only read as "not on the picture". So a photo was openable by clicking
        // the left two thirds of it, and by nothing you could do to the right third, which is the sort of thing
        // that gets reported as "sometimes" because it depends on where the hand happened to land.
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", Red(140, 90)));

            // A 140 x 90 photo is under the 160 pixel edge, so it is shown at exactly 140 x 90.
            var corner = ThumbnailCorner(form, "sunset.png");

            DoubleClickTranscriptAt(form, corner.X + 136, corner.Y + 45);

            var opened = form.OwnedForms.OfType<PictureViewerForm>().ToList();

            Assert.Single(opened);

            opened[0].Close();
        });
    }

    [Fact]
    public void ADoubleClickBelowAThumbnailOpensNothing()
    {
        // The other side of the same rule, and the reason the answer is not "open the photo whenever the character
        // after it is reported": a thumbnail makes its line as tall as itself, so there is a band of empty
        // transcript under the photo that belongs to no words, and a click down there is a click on nothing.
        // Doing something there would be the same fault pointed the other way.
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new PictureMessage("User 2", "Room 1", "sunset.png", Red(140, 90)));
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "the next entry"));

            var corner = ThumbnailCorner(form, "sunset.png");

            DoubleClickTranscriptAt(form, corner.X + 70, corner.Y + 90 + 18);

            Assert.Equal(0, form.OwnedForms.Count(f => f is PictureViewerForm));
        });
    }

    /// <summary>
    /// The top-left pixel of a photo's own thumbnail, which is where its character is.
    /// </summary>
    private static Point ThumbnailCorner(Control form, string file)
    {
        var rtb = (RichTextBox)Find(form, "rtbChat")!;
        var words = rtb.Text.IndexOf(file, StringComparison.Ordinal);

        Assert.True(words >= 0, $"there is no {file} in the transcript");

        // The thumbnail goes on the line below the entry's words.
        var photo = rtb.GetFirstCharIndexFromLine(rtb.GetLineFromCharIndex(words) + 1);

        Assert.True(photo > words, "the line under the caption is not the photo's");

        return rtb.GetPositionFromCharIndex(photo);
    }

    /// <summary>Raises the transcript's double-click at a pixel, by the control's own coordinates.</summary>
    private static void DoubleClickTranscriptAt(Control form, int x, int y)
    {
        var rtb = (RichTextBox)Find(form, "rtbChat")!;

        var raise = typeof(Control).GetMethod(
            "OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(raise);

        raise!.Invoke(rtb, new object?[]
        {
            new MouseEventArgs(MouseButtons.Left, 2, x, y, 0),
        });

        for (var i = 0; i < 8; i++)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    // ------------------------------------------------------------------ harness

    /// <summary>
    /// Raises the transcript's double-click the way the framework does, at wherever the given text is.
    /// </summary>
    /// <remarks>
    /// A posted <c>WM_LBUTTONDBLCLK</c> does not make WinForms raise <c>MouseDoubleClick</c> - it wants real
    /// input, and was checked to not do it - so this asks the control to do what a double click does. It is the
    /// last metre of the affordance: designer wiring that goes missing, or a handler that stops opening anything,
    /// is invisible to the tests that call <c>TryOpenPictureAt</c> and to the ones that override
    /// <c>OpenPicture</c> and record instead of opening.
    /// </remarks>
    private static void DoubleClickTranscriptAt(Control form, string text)
    {
        var rtb = (RichTextBox)Find(form, "rtbChat")!;
        var index = rtb.Text.IndexOf(text, StringComparison.Ordinal);
        Assert.True(index >= 0, $"there is no {text} in the transcript to double-click");

        var where = rtb.GetPositionFromCharIndex(index);

        var raise = typeof(Control).GetMethod(
            "OnMouseDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(raise);

        raise!.Invoke(rtb, new object?[]
        {
            new MouseEventArgs(MouseButtons.Left, 2, where.X, where.Y, 0),
        });

        for (var i = 0; i < 8; i++)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private static Control? Find(Control parent, string name)
        => parent.Controls.Find(name, searchAllChildren: true).FirstOrDefault();

    private static ListBox Rooms(Control form) => (ListBox)Find(form, "lstRooms")!;

    /// <summary>A red photo of a known size, so a pixel can be checked against it.</summary>
    private static byte[] Red(int width, int height)
    {
        using var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Red);

        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }
}
