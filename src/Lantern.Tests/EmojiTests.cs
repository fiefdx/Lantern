using System.Drawing.Imaging;
using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// Emoji in what people type.
/// </summary>
/// <remarks>
/// <para>
/// Nothing in Lantern was rejecting an emoji: they went into the box, into the room and onto the wire, and this
/// suite never once looked at what came out. Two things were actually wrong or unproven, and only one of them needed
/// a code change.
/// </para>
/// <para>
/// <b>Length was counted in UTF-16 units while it is read as characters.</b> A smiley is two units, so
/// <see cref="ValidationRules.MessageMaxLength"/> of 500 meant 250 of them, a nickname of 16 units meant 8 faces,
/// and the refusal said "must be 500 characters or fewer" to somebody counting half that many. That is the sort of
/// message that teaches a person the program is arbitrary. Now the ruler is <see cref="ValidationRules.Length"/>:
/// one emoji, one character - and a family joined with zero-width joiners, and a flag made of two regional
/// indicators, are one character each, because that is what somebody looking at them counts.
/// </para>
/// <para>
/// <b>Whether they were drawn was unmeasured.</b> A RichTextBox is a GDI control, so it has no colour font
/// machinery of its own; what it does was found out here rather than assumed. The tests below put ink on a bitmap
/// and compare the emoji's cell against a character Windows definitely has no glyph for, because "something was
/// drawn" is also true of an empty box.
/// </para>
/// <para>
/// What they cannot decide is whether this machine paints the faces in colour or in outline. That question was
/// settled afterwards, by a person with eyes and a screen, and the answer was "outline, everywhere" - which is what
/// led to <see cref="ColorEmojiTests"/> and to the faces in a message being drawn as pictures rather than as text.
/// What is left here is the text half of the promise: an emoji is still counted as one character, still travels
/// whole, and is still drawn as something rather than nothing wherever it is written as text - in the box, and in a
/// headline.
/// </para>
/// </remarks>
public sealed class EmojiTests
{
    private const string Smiley = "\U0001F600";             // 😀
    private const string Party = "\U0001F389";               // 🎉
    private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467"; // 👨‍👩‍👧
    private const string Flag = "\U0001F1FB\U0001F1F3";       // 🇻🇳

    /// <summary>A code point Windows is guaranteed to have no glyph for: private use area.</summary>
    private const string NoGlyph = "\uE123";

    // ------------------------------------------------------------------ the ruler

    [Theory]
    [InlineData("", 0)]
    [InlineData("hi", 2)]
    [InlineData("cà phê", 6)]
    [InlineData(Smiley, 1)]
    [InlineData(Smiley + Party, 2)]
    [InlineData("a" + Smiley + "b", 3)]
    [InlineData(Family, 1)]      // three people and two joiners: one picture, one character
    [InlineData(Flag, 1)]        // two regional indicators: one flag
    [InlineData("😀́", 1)]         // a face with a combining mark on it
    public void OneThingSeenIsOneCharacterCounted(string text, int expected)
        => Assert.Equal(expected, ValidationRules.Length(text));

    [Fact]
    public void FiveHundredEmojiAreAMessageAndOneMoreIsNot()
    {
        Assert.Null(ValidationRules.MessageError(string.Concat(Enumerable.Repeat(Smiley, 500))));

        var tooMany = ValidationRules.MessageError(string.Concat(Enumerable.Repeat(Smiley, 501)));

        // The reason has to be countable by the person reading it. Saying "500" to somebody holding 501 faces is
        // fair; saying it to somebody who was told their 250 faces were too long is not.
        Assert.NotNull(tooMany);
        Assert.Contains("500", tooMany!, StringComparison.Ordinal);
    }

    [Fact]
    public void SixteenEmojiAreASixteenCharacterNickname()
    {
        Assert.Null(ValidationRules.NicknameError(string.Concat(Enumerable.Repeat(Smiley, 16))));
        Assert.NotNull(ValidationRules.NicknameError(string.Concat(Enumerable.Repeat(Smiley, 17))));

        Assert.Null(ValidationRules.RoomNameError(string.Concat(Enumerable.Repeat(Party, 24))));
        Assert.NotNull(ValidationRules.RoomNameError(string.Concat(Enumerable.Repeat(Party, 25))));
    }

    [Fact]
    public void AnEmojiIsNotRewrittenOnTheWayThrough()
        => Assert.Equal("cà phê " + Smiley, ValidationRules.Normalize("  cà   phê " + Smiley + "  "));

    // ------------------------------------------------------------------ the window

    [Fact]
    public void WhatWasTypedWithEmojiIsSentAndShownAsItWasTyped()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            // The box is where the Windows picker (Win + .) puts an emoji, and where a paste lands. Both arrive
            // whole; nothing here strips or escapes them.
            var typed = "cà phê " + Smiley + Party;
            Box(form).Text = typed;
            Send(form);

            var sent = Assert.Single(service.Transcript.OfType<TextMessage>(), m => m.Body.Contains(Smiley, StringComparison.Ordinal));
            Assert.Equal(typed, sent.Body);

            // The words are the control's text; the two faces in that sentence are not, because since v1.5 they are
            // drawn as pictures - see ColorEmojiTests for the trade and for the picture's own assertions.
            Assert.Contains("cà phê ", Transcript(form), StringComparison.Ordinal);
            Assert.Equal(string.Empty, Box(form).Text);
        });
    }

    /// <remarks>
    /// Since v1.5 a face in the body of a message is a picture, so it is not in the control's text and has no cell
    /// of its own to look at. A face where this one looks is the one in a headline, and in the box, and on any
    /// machine with no colour renderer - which is to say: this pair now measures the path where an emoji really is
    /// drawn as a glyph, and it is the path that must not quietly go blank.
    /// </remarks>
    [Fact]
    public void TheWindowDrawsSomethingWhereAnEmojiIsWrittenAsText()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;
            service.SimulateIncoming(new TextMessage("User 2" + Smiley, "Room 1", "hello"));

            using var ink = CellOf(Rt(form), Smiley);

            // Not nothing. A control that silently draws no glyph for a code point it was handed leaves the
            // sentence with a hole in it and says nothing about it, which is the failure this measures.
            Assert.True(InkIn(ink) > 15, $"the emoji's cell has {InkIn(ink)} ink pixels in it");
        });
    }

    [Fact]
    public void AnEmojiWrittenAsTextIsNotTheBoxWindowsUsesForWhatItCannotDraw()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var withEmoji = new HostForm(service);

            withEmoji.Show();
            Rooms(withEmoji).SelectedIndex = 0;
            service.SimulateIncoming(new TextMessage("User 2" + Smiley, "Room 1", "hello"));

            using var emoji = CellOf(Rt(withEmoji), Smiley);

            // A private-use code point has no glyph anywhere, so Windows draws its missing-glyph box there.
            // If the smiley's cell were the same picture, the transcript would be full of boxes and every test
            // above about "ink" would still pass - which is the whole reason this one exists.
            var other = new FakeChatService("Haitao");
            using var withBox = new HostForm(other);

            withBox.Show();
            Rooms(withBox).SelectedIndex = 0;
            other.SimulateIncoming(new TextMessage("User 2" + NoGlyph, "Room 1", "hello"));

            using var box = CellOf(Rt(withBox), NoGlyph);

            Assert.True(Differs(emoji, box) > 20, "the emoji is being drawn as the same shape as a character with no glyph");
        });
    }

    [Fact]
    public void AnEmojiNicknameIsShownInTheMemberList()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");

            using var form = new HostForm(service);

            form.Show();
            Rooms(form).SelectedIndex = 0;

            // After the room is entered, because entering it is what refills the list - and the list arriving is
            // what the window redraws on. A nickname is the one place a person may choose to be an emoji, and the
            // member list is a plain ListBox, which draws its text by the same GDI route as the transcript.
            service.Members.Add(new User("c-emoji", "Xanh " + Smiley));
            Thread.Sleep(50);
            Application.DoEvents();

            Assert.Contains(
                Members(form).Items.Cast<object>().Select(i => i.ToString()),
                t => t is not null && t.Contains("Xanh", StringComparison.Ordinal) && t.Contains(Smiley, StringComparison.Ordinal));
        });
    }

    // ------------------------------------------------------------------ harness

    private static RichTextBox Rt(Control form) => (RichTextBox)form.Controls.Find("rtbChat", true)[0]!;

    private static TextBox Box(Control form) => (TextBox)form.Controls.Find("txtMessage", true)[0]!;

    private static ListBox Rooms(Control form) => (ListBox)form.Controls.Find("lstRooms", true)[0]!;

    private static ListBox Members(Control form) => (ListBox)form.Controls.Find("lstMembers", true)[0]!;

    private static string Transcript(Control form) => Rt(form).Text;

    private static void Send(Control form) => ((Button)form.Controls.Find("btnSend", true)[0]!).PerformClick();

    /// <summary>
    /// The part of the transcript where an entry's own text sits: the cell just under and right of wherever the
    /// given text begins, big enough for one glyph and nothing else.
    /// </summary>
    private static Bitmap CellOf(RichTextBox rtb, string text)
    {
        var index = TranscriptOf(rtb).IndexOf(text, StringComparison.Ordinal);
        Assert.True(index >= 0, $"the transcript does not contain {text} to look at");

        var at = rtb.GetPositionFromCharIndex(index);

        return Capture(rtb, new Rectangle(at.X, at.Y, 24, 20));
    }

    private static string TranscriptOf(Control c) => ((RichTextBox)c).Text;

    private static Bitmap Capture(Control control, Rectangle cell)
    {
        var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        var cropped = new Bitmap(cell.Width, cell.Height);

        for (var y = 0; y < cell.Height; y++)
        {
            for (var x = 0; x < cell.Width; x++)
            {
                var within = new Point(cell.X + x, cell.Y + y);

                if (within.X < bitmap.Width && within.Y < bitmap.Height)
                {
                    cropped.SetPixel(x, y, bitmap.GetPixel(within.X, within.Y));
                }
            }
        }

        bitmap.Dispose();
        return cropped;
    }

    private static int InkIn(Bitmap bitmap)
    {
        var ink = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var p = bitmap.GetPixel(x, y);
                if (p.R < 200 || p.G < 200 || p.B < 200)
                {
                    ink++;
                }
            }
        }

        return ink;
    }

    private static int Differs(Bitmap one, Bitmap two)
    {
        var different = 0;

        for (var y = 0; y < Math.Min(one.Height, two.Height); y++)
        {
            for (var x = 0; x < Math.Min(one.Width, two.Width); x++)
            {
                if (one.GetPixel(x, y) != two.GetPixel(x, y))
                {
                    different++;
                }
            }
        }

        return different;
    }
}
