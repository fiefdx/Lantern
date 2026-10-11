using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Choosing an emoji inside Lantern, rather than hoping the operating system offers its popup.
/// </summary>
/// <remarks>
/// <para>
/// The report was that Win + . does not pop up. Lantern cannot be the reason - the shell takes Win combinations
/// before an application is told anything, and this window's only key handlers are Ctrl+N, Ctrl+D and Enter - and
/// the machine was checked anyway: text-input service running, both processes that draw the panel running, no
/// policy on the hotkey, the app not elevated. So the feature was standing on somebody else's program, which is
/// also the case where it is definitely unreachable: over a remote desktop the local session keeps Win + . and the
/// window being typed into never sees it.
/// </para>
/// <para>
/// A table of faces of our own is the answer, and it has one design constraint that matters more than its looks:
/// putting something into the box by way of <see cref="TextBox.SelectedText"/> walks straight past
/// <see cref="TextBox.MaxLength"/>, which only ever guards keystrokes. So the picker counts, in the characters a
/// person counts, and refuses out loud rather than truncating a sentence.
/// </para>
/// </remarks>
public sealed class EmojiPickerTests
{
    [Fact]
    public void TheChatWindowHasAButtonThatOpensTheTable()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var button = Button(form);

            Assert.Equal("Emoji", button.Text);
            Assert.True(button.Enabled, "the emoji button is shut while a message could be written");

            // The button wears a face drawn by the code that colours them in the transcript: what is clicked and
            // what arrives are the same picture.
            Assert.NotNull(button.Image);
        });
    }

    [Fact]
    public void TheButtonIsShutWhenThereIsNoRoomToWriteIn()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();

            // No room entered: the box, Share and Send are shut, and so is this - a face picked for no room would
            // sit in a box that cannot send it.
            Assert.False(Button(form).Enabled);
        });
    }

    [Fact]
    public void PressingItPutsTheTableOnScreenBelongingToThisWindow()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            Button(form).PerformClick();
            Pump();

            // Owned, not modal: the room stays usable while the table is down, and closing the chat window takes
            // the table with it.
            var picker = Assert.Single(form.OwnedForms.OfType<EmojiPickerForm>());
            Assert.Same(form, picker.Owner);
            Assert.True(picker.Visible);
            picker.Close();
        });
    }

    [Fact]
    public void TheTableIsATableAndEveryTileIsOneCharacter()
    {
        Sta.Run(() =>
        {
            var picker = new EmojiPickerForm();

            Assert.Equal(EmojiPickerForm.Elements.Count, picker.Tiles.Count);
            Assert.NotEmpty(picker.Tiles);

            var grid = Assert.IsType<TableLayoutPanel>(picker.Tiles[0].Parent);
            Assert.True(grid.ColumnCount >= 6, "the faces are not laid out in columns, so it is a list and not a table");

            foreach (var element in EmojiPickerForm.Elements)
            {
                // Two things the table must never get wrong: offering something that is several characters by the
                // ruler a message is counted with, and offering something that is not an emoji at all - a letter
                // offered here would arrive as a picture in the transcript.
                Assert.Equal(1, ValidationRules.Length(element));
                Assert.True(ColorEmoji.IsEmoji(element), $"{element} is offered as an emoji but is not one");
            }

            picker.Close();
        });
    }

    [Fact]
    public void ATileIsADrawnColourFaceAndNotAnOutlineOne()
    {
        Sta.Run(() =>
        {
            var picker = new EmojiPickerForm();

            var image = picker.Tiles[0].Image;
            Assert.NotNull(image);

            Assert.True(Coloured(image!) > 15, $"the first tile has {Coloured(image!)} coloured pixels in it");

            picker.Close();
        });
    }

    [Fact]
    public void ClickingATilePutsTheFaceInTheMessageWhereTheCaretWas()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var box = Box(form);
            box.Text = "chuc mung";
            box.SelectionStart = 4;

            var picker = Open(form);
            var element = EmojiPickerForm.Elements[0];
            picker.Tiles[0].PerformClick();
            Pump();

            Assert.Equal("chuc" + element + " mung", box.Text);

            // The caret ends up after the face, so a second one can be clicked straight after it rather than
            // every pick having to be followed by reaching for the arrow keys.
            Assert.Equal(4 + element.Length, box.SelectionStart);
            Assert.Equal(0, box.SelectionLength);
        });
    }

    [Fact]
    public void AChosenFaceIsSentLikeAnythingElseWasSent()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var element = EmojiPickerForm.Elements[0];
            Pick(form, element);
            Send(form);

            // Send is asynchronous; a test that only clicks has not waited for the room to hear about it.
            Pump();

            var sent = Assert.Single(service.Transcript.OfType<TextMessage>(), m => m.Body.Contains(element, StringComparison.Ordinal));
            Assert.Equal(element, sent.Body);

            // And it reaches the room as a picture, because that is how a face is drawn since v1.5: the picker is
            // an input method, not a different kind of message.
            Assert.Single(PicturesIn(Rt(form)));
        });
    }

    [Fact]
    public void TheTableRefusesToOverflowTheMessageAndSaysSo()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var box = Box(form);

            // MaxLength guards a keystroke, not an assignment to the selection - so a picker is exactly the way
            // past it, and this is the boundary where that would either silently eat a character or say nothing.
            box.Text = new string('a', ValidationRules.MessageMaxLength);

            Pick(form, EmojiPickerForm.Elements[0]);

            Assert.Equal(ValidationRules.MessageMaxLength, ValidationRules.Length(box.Text));

            var said = Status(form);
            Assert.Contains(ValidationRules.MessageMaxLength.ToString(), said, StringComparison.Ordinal);
            Assert.Contains("would be", said, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ClosingTheTableChangesNothingAndDoesNotSendAnEmptyMessage()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var picker = Open(form);

            // Escape, or the Close button: the window is wired so that both mean "nothing chosen". CancelButton is
            // what makes Escape do it, and that is a designer line a later edit can lose.
            Assert.NotNull(picker.CancelButton);

            picker.Close();
            Pump();

            Assert.Empty(form.OwnedForms);
            Assert.Equal(string.Empty, Box(form).Text);
            Assert.DoesNotContain(service.Transcript.OfType<TextMessage>(), m => m.Body.Length == 0);
        });
    }

    [Fact]
    public void FacesCanBeClickedOneAfterAnotherWithoutReachingForTheKeyboard()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            var box = Box(form);
            var first = EmojiPickerForm.Elements[0];
            var second = EmojiPickerForm.Elements[1];

            Pick(form, first);
            Pick(form, second);

            Assert.Equal(first + second, box.Text);
        });
    }

    // ------------------------------------------------------------------ helpers

    private static RichTextBox Rt(Control form) => (RichTextBox)form.Controls.Find("rtbChat", true)[0];

    private static ListBox Rooms(Control form) => (ListBox)form.Controls.Find("lstRooms", true)[0];

    private static TextBox Box(Control form) => (TextBox)form.Controls.Find("txtMessage", true)[0];

    private static Button Button(Control form) => (Button)form.Controls.Find("btnEmoji", true)[0];

    private static void Send(Control form) => ((Button)form.Controls.Find("btnSend", true)[0]!).PerformClick();

    private static EmojiPickerForm Open(Form form)
    {
        Button(form).PerformClick();
        Pump();

        return Assert.Single(form.OwnedForms.OfType<EmojiPickerForm>());
    }

    /// <summary>Opens the table, clicks the tile showing this face, and waits for the window to be gone.</summary>
    private static void Pick(Form form, string element)
    {
        var picker = Open(form);
        var tile = picker.Tiles.First(t => (string)t.Tag! == element);

        // A tile only raises its click if it could really be clicked, which needs a drawn window (see ChatFormTests).
        tile.PerformClick();
        Pump();

        Assert.Empty(form.OwnedForms);
    }

    private static string Status(Control form)
    {
        var strip = (StatusStrip)form.Controls.Find("statusStrip", true)[0];

        // What the bar is saying, ignoring the labels that are saying nothing - including the file progress one,
        // which is empty until something is being shared.
        return string.Join(" | ", strip.Items.OfType<ToolStripStatusLabel>()
            .Select(item => item.Text)
            .Where(text => !string.IsNullOrEmpty(text)));
    }

    private static int Coloured(Image image)
    {
        var bitmap = new Bitmap(image);
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

        bitmap.Dispose();
        return count;
    }

    private static List<(int Width, int Height)> PicturesIn(RichTextBox rtb)
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

    private static void Pump()
    {
        for (var i = 0; i < 8; i++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}
