using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// F13: what a message looks like in the transcript. In a group chat an entry is read as a log, so the
/// <em>when</em> and <em>who</em> go on a headline of their own and what was actually said starts on the line
/// below, in another colour.
/// <para>
/// These are answered on <see cref="TranscriptStyle"/> where the question is about the rule and on a real
/// window where the question is whether the window keeps it - the welcome line nobody sent, a notice about
/// somebody joining, a rebuilt transcript after a room switch: everything the transcript draws, and not only
/// the messages a person typed.
/// </para>
/// </summary>
public class TranscriptLayoutTests
{
    /// <summary>A time with a date nobody would mistake for another one, and a clock time that reads the same at any locale.</summary>
    private static readonly DateTime SentAt = new(2026, 3, 7, 14, 5, 0);

    [Fact]
    public void AMessageHasAHeadlineOfItsOwnAndItsBodyOnTheLineBelow()
    {
        Sta.Run(() =>
        {
            var lines = TranscriptOf(new TextMessage("User 2", "Room 1", "the body", sentAt: SentAt));

            Assert.Contains("[2026-03-07 14:05] User 2", lines, StringComparer.Ordinal);
            Assert.Contains("the body", lines, StringComparer.Ordinal);

            // Adjacent, and in that order: the headline belongs to the message under it, and a reader who has
            // to hunt for which headline a line belongs to is reading a transcript that lost the plot.
            var headline = Array.FindIndex(lines, l => l == "[2026-03-07 14:05] User 2");
            var body = Array.FindIndex(lines, l => l == "the body");

            Assert.True(headline >= 0 && body >= 0, "the entry is not in the transcript at all");
            Assert.Equal(headline + 1, body);
        });
    }

    [Fact]
    public void TheHeadlineCarriesTheDateBecauseAChatStaysOpenAcrossAnAfternoon()
    {
        var message = new TextMessage("User 2", "Room 1", "was that today?", sentAt: SentAt);

        Assert.Contains("2026-03-07", TranscriptStyle.Headline(message), StringComparison.Ordinal);
        Assert.Contains("14:05", TranscriptStyle.Headline(message), StringComparison.Ordinal);

        // The clock time alone cannot answer "when did that arrive?" once the chat has been open since the
        // morning, which is exactly when somebody asks it.
        Assert.DoesNotContain("was that today?", TranscriptStyle.Headline(message), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSenderIsNotOnTheLineTheySaid()
    {
        Sta.Run(() =>
        {
            var lines = TranscriptOf(new TextMessage("User 2", "Room 1", "hello there", sentAt: SentAt));

            // The old shape put both on one line, which is what the change is against: in a room with four
            // people the names in the middle of every line are what makes the text unreadable.
            var body = Assert.Single(lines, line => line == "hello there");

            Assert.DoesNotContain("User 2", body, StringComparison.Ordinal);
            Assert.DoesNotContain("[2026", body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EveryKindOfMessageHasADifferentColourForItsHeadlineAndItsContent()
    {
        var messages = new Message[]
        {
            new TextMessage("User 2", "Room 1", "text", sentAt: SentAt),
            new PictureMessage("User 2", "Room 1", "photo.png", [], sentAt: SentAt),
            new FileMessage("User 2", "Room 1", "notes.txt", 12, Guid.NewGuid(), sentAt: SentAt),
            SystemMessage.Join("Room 1", "User 3"),
            SystemMessage.Leave("Room 1", "User 3"),
            new SystemMessage(string.Empty, "a notice with no sender"),
        };

        foreach (var message in messages)
        {
            Assert.NotEqual(TranscriptStyle.HeadlineColorFor(message), TranscriptStyle.ContentColorFor(message));
        }
    }

    [Fact]
    public void TheWindowActuallyPaintsAHeadlineInADifferentColourFromTheMessage()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);

            // Reading a colour back out of a RichTextBox is asking the window, so these tests need one.
            form.Show();

            var chat = (RichTextBox)Find(form, "rtbChat")!;

            ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "painted in two colours", sentAt: SentAt));

            var headline = ColourOf(chat, "[2026-03-07 14:05] User 2");
            var content = ColourOf(chat, "painted in two colours");

            Assert.NotEqual(headline, content);

            // And the colours are the ones the style decided, so a change to either has to be a decision
            // rather than a window quietly disagreeing with the rule. Compared by value: the style says
            // "the window's text colour" and the window answers "black", which is the same colour wearing
            // different names.
            Assert.Equal(TranscriptStyle.HeadlineColor.ToArgb(), headline.ToArgb());
            Assert.Equal(TranscriptStyle.ContentColor.ToArgb(), content.ToArgb());
        });
    }

    [Fact]
    public void ANoticeIsShapedLikeAMessageAndQuieterThanOne()
    {
        var notice = SystemMessage.Join("Room 1", "User 3");

        // Shaped the same: it is still an entry with a headline and a line under it. Quieter: grey where a
        // person's headline is blue, because "User 3 joined" is not somebody speaking to the room.
        Assert.StartsWith("[", TranscriptStyle.Headline(notice), StringComparison.Ordinal);
        Assert.DoesNotContain("User 3", TranscriptStyle.Headline(notice), StringComparison.Ordinal);
        Assert.Contains("User 3", TranscriptStyle.Content(notice), StringComparison.Ordinal);

        Assert.Equal(TranscriptStyle.NoticeHeadlineColor, TranscriptStyle.HeadlineColorFor(notice));
        Assert.Equal(TranscriptStyle.NoticeContentColor, TranscriptStyle.ContentColorFor(notice));

        Assert.NotEqual(TranscriptStyle.NoticeHeadlineColor, TranscriptStyle.HeadlineColor);
    }

    [Fact]
    public void TheWelcomeLineIsAnEntryToo()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);
            form.Show();

            var chat = (RichTextBox)Find(form, "rtbChat")!;

            // Nobody sent the welcome line, so its headline is the time and nothing else - there is no sender
            // to invent, and a headline with a made-up name in it would be a lie about who was in the room.
            Assert.Contains(chat.Lines, line => line.Contains("Welcome, Haitao!", StringComparison.Ordinal));
            Assert.DoesNotContain(chat.Lines, line => line.StartsWith("[", StringComparison.Ordinal) && line.Contains("Lantern:", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ARebuiltTranscriptUsesTheSameLayoutAsOneBeingReadLive()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "first in", sentAt: SentAt));
            service.SimulateIncoming(new TextMessage("User 3", "Room 1", "second in", sentAt: SentAt));

            rooms.SelectedIndex = 1;
            rooms.SelectedIndex = 0;

            // The transcript is rebuilt from the room's own history whenever somebody switches rooms, and the
            // rebuild has to go through the same layout as the live path or the shape changes on the way back.
            // Not a comparison of the whole transcript: leaving a room is announced, so coming back to it
            // legitimately shows more entries than were typed (F11).
            AssertHeadlineAbove(chat, "first in", "User 2");
            AssertHeadlineAbove(chat, "second in", "User 3");
        });
    }

    [Fact]
    public void EveryMessageInAConversationIsHeadlinedByWhoSentIt()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);
            form.Show();

            var rooms = (ListBox)Find(form, "lstRooms")!;
            var chat = (RichTextBox)Find(form, "rtbChat")!;

            rooms.SelectedIndex = 0;
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "one", sentAt: SentAt));
            service.SimulateIncoming(new TextMessage("User 3", "Room 1", "two", sentAt: SentAt));
            service.SimulateIncoming(new TextMessage("User 2", "Room 1", "three", sentAt: SentAt));

            // Each line under the headline of the person who typed it, including the second one from the same
            // person: a headline is per message, not per person in the room.
            AssertHeadlineAbove(chat, "one", "User 2");
            AssertHeadlineAbove(chat, "two", "User 3");
            AssertHeadlineAbove(chat, "three", "User 2");
        });
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// The transcript lines a window shows for one message, blank lines left out.
    /// </summary>
    /// <remarks>
    /// The window is disposed rather than closed, like the rest of the UI suite: closing a <see cref="HostForm"/>
    /// asks whether the person really means it - which is the correct behaviour in front of a person (F: a host
    /// disconnects everybody) and a modal sitting in front of a test runner, which waits thirty seconds for
    /// nobody. Tests that are about closing override the asking (see <c>ClosingTheChatWindowTests</c>).
    /// </remarks>
    [Fact]
    public void TheTranscriptIsWrittenOnWhiteLikeTheRestOfTheWindow()
    {
        // A read-only text area paints itself with SystemColors.Control - the window's grey - so leaving the
        // background alone produced a grey chatting area between a white room list, a white member list and a
        // white message box: four surfaces, three of them alike, and the odd one out is the one everybody looks
        // at. Reported as "the chatting area background should be white", and it was right.
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new HostForm(service);
            form.Show();

            var transcript = (RichTextBox)Find(form, "rtbChat")!;

            // By value, not by reference: SystemColors.Window and Color.White are the same colour wearing
            // different names, and a test that tells them apart is testing the CLR's colour book rather than
            // what a person sees.
            Assert.Equal(Color.White.ToArgb(), transcript.BackColor.ToArgb());
            Assert.Equal(Color.Black.ToArgb(), transcript.ForeColor.ToArgb());
            Assert.Equal(TranscriptStyle.BackgroundColor.ToArgb(), transcript.BackColor.ToArgb());

            // One surface, decided once: whatever the list beside it and the box below it are painted, that is
            // what the conversation is painted too.
            Assert.Equal(((ListBox)Find(form, "lstRooms")!).BackColor.ToArgb(), transcript.BackColor.ToArgb());
            Assert.Equal(((ListBox)Find(form, "lstMembers")!).BackColor.ToArgb(), transcript.BackColor.ToArgb());
            Assert.Equal(((RichTextBox)Find(form, "txtMessage")!).BackColor.ToArgb(), transcript.BackColor.ToArgb());

            form.Dispose();
        });
    }

    [Fact]
    public void EveryColourInTheTranscriptCanBeReadOnIt()
    {
        // The inks and the paper are pinned together, which is what makes this measurable instead of a matter of
        // taste. 4.5:1 is the ordinary bar for text a person has to read at arm's length - and the front of a lab
        // has a projector in it.
        var paper = TranscriptStyle.BackgroundColor;

        Readable(TranscriptStyle.ContentColor, paper, "what somebody typed");
        Readable(TranscriptStyle.HeadlineColor, paper, "who said it, and when");
        Readable(TranscriptStyle.NoticeContentColor, paper, "a join or leave notice");
        Readable(TranscriptStyle.NoticeHeadlineColor, paper, "the time of a notice");
    }

    private static void Readable(Color ink, Color paper, string what)
    {
        var ratio = Contrast(ink, paper);
        Assert.True(
            ratio >= 4.5,
            $"{what} in #{ink.ToArgb() & 0xFFFFFF:X6} on #{paper.ToArgb() & 0xFFFFFF:X6} is {ratio:F1}:1; 4.5:1 is what reading costs");
    }

    /// <summary>The WCAG contrast ratio: (lighter + 0.05) over (darker + 0.05), each channel on a linear scale.</summary>
    private static double Contrast(Color a, Color b)
    {
        static double Linear(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

        static double Luminance(Color c)
            => 0.2126 * Linear(c.R / 255.0) + 0.7152 * Linear(c.G / 255.0) + 0.0722 * Linear(c.B / 255.0);

        var first = Luminance(a);
        var second = Luminance(b);
        var (lighter, darker) = first > second ? (first, second) : (second, first);

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static string[] TranscriptOf(Message message)
    {
        var service = new FakeChatService("Haitao");
        using var form = new HostForm(service);
        form.Show();

        var rooms = (ListBox)Find(form, "lstRooms")!;
        var chat = (RichTextBox)Find(form, "rtbChat")!;

        rooms.SelectedIndex = 0;
        service.SimulateIncoming(message);

        return chat.Lines.Where(line => line.Length != 0).ToArray();
    }

    /// <summary>
    /// Asserts that a line in the transcript sits directly under a headline naming <paramref name="sender"/>.
    /// Asserted per message rather than by counting lines, because the transcript also holds notices, welcome
    /// lines and files - and the rule is about each entry, not about how many lines the window happens to show.
    /// </summary>
    private static void AssertHeadlineAbove(RichTextBox chat, string body, string sender)
    {
        var lines = chat.Lines;
        var at = Array.FindIndex(lines, line => line == body);

        Assert.True(at > 0, "the transcript does not contain " + body + " on a line of its own");
        Assert.StartsWith("[", lines[at - 1], StringComparison.Ordinal);
        Assert.EndsWith(sender, lines[at - 1], StringComparison.Ordinal);
    }

    /// <summary>
    /// The colour a stretch of the transcript is painted in, read back from the window by selecting it - which
    /// is what the window itself reports, rather than what a guess about its RTF says.
    /// </summary>
    private static Color ColourOf(RichTextBox chat, string text)
    {
        var at = chat.Text.LastIndexOf(text, StringComparison.Ordinal);

        Assert.True(at >= 0, "the transcript does not contain " + text);

        chat.SelectionStart = at;
        chat.SelectionLength = text.Length;

        try
        {
            return chat.SelectionColor;
        }
        finally
        {
            chat.SelectionLength = 0;
        }
    }

    private static Control? Find(Control root, string name)
        => root.Controls.Find(name, searchAllChildren: true).FirstOrDefault();
}
