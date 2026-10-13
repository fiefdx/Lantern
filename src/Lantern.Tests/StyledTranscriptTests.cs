using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Lantern.Core;
using Lantern.Domain;
using Lantern.UI;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T12.2: a run is not a decoration in the sender's box. What was typed bold, big and red has to arrive that
/// way, in both windows, after a room switch, with a face in the middle of it.
/// </summary>
public class StyledTranscriptTests
{
    private const string Face = "\U0001F642";

    [Fact]
    public void AStyledWordArrivesInBold()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            Receive(service, Styled("User 2", "Room 1", "READ THIS now",
                new StyledRun(9, Bold: true), new StyledRun(4)));

            Assert.True(BoldAt(chat, BodyIndex(chat, "READ THIS now")), "the styled run did not arrive bold");
            Assert.False(BoldAt(chat, BodyIndex(chat, "READ THIS now") + 10),
                "the plain run after it went bold as well");
        });
    }

    [Fact]
    public void AStyledWordArrivesInItalicAndUnderline()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            Receive(service, Styled("User 2", "Room 1", "quietly loud",
                new StyledRun(7, Italic: true, Underline: true), new StyledRun(5)));

            var at = BodyIndex(chat, "quietly loud");
            var style = StyleAt(chat, at);

            Assert.True(style.HasFlag(FontStyle.Italic), "not italic");
            Assert.True(style.HasFlag(FontStyle.Underline), "not underlined");
            Assert.False(BoldAt(chat, at + 8), "the plain word took the formatting");
        });
    }

    [Fact]
    public void AStyledWordArrivesInItsColour()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            Receive(service, Styled("User 2", "Room 1", "look here",
                new StyledRun(4, Color: "#C00000"), new StyledRun(5)));

            Assert.Equal(Color.FromArgb(0xC0, 0x00, 0x00), ColourAt(chat, BodyIndex(chat, "look here")));
            Assert.True(TranscriptStyle.ContentColor == ColourAt(chat, BodyIndex(chat, "look here") + 5),
                "the rest of the line took the colour too");
        });
    }

    [Fact]
    public void ARunMarkedLargeIsTallerThanTheLineAroundIt()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            Receive(service, Styled("User 2", "Room 1", "small normal BIG",
                new StyledRun(6, Size: StyledRun.Small),
                new StyledRun(7),
                new StyledRun(3, Size: StyledRun.Large)));

            var at = BodyIndex(chat, "small normal BIG");
            var @base = SizeAt(chat, at + 7);

            Assert.True(SizeAt(chat, at) < @base, $"small came through at {SizeAt(chat, at)} against a base of {@base}");
            Assert.True(SizeAt(chat, at + 13) > @base, $"large came through at {SizeAt(chat, at + 13)} against a base of {@base}");
        });
    }

    [Fact]
    public void APlainLineIsStillDrawnPlain()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            Receive(service, new TextMessage("User 2", "Room 1", "nothing at all here"));

            var at = BodyIndex(chat, "nothing at all here");

            Assert.Equal(FontStyle.Regular, StyleAt(chat, at));
            Assert.Equal(chat.Font.Size, SizeAt(chat, at), 1);
            Assert.Equal(TranscriptStyle.ContentColor, ColourAt(chat, at));
        });
    }

    [Fact]
    public void TheHeadlineIsNeverStyled()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            Receive(service, Styled("User 2", "Room 1", "shouting",
                new StyledRun(8, Bold: true, Size: StyledRun.Large, Color: "#FF0000")));

            // Back over the break above the body, then back along the headline to its first character. The first
            // line of a room is somebody's join notice, and that is drawn in notice colours rather than this one's.
            var at = chat.Text.LastIndexOfAny(new[] { '\r', '\n' }, BodyIndex(chat, "shouting") - 1);

            while (at > 0 && chat.Text[at - 1] != '\r' && chat.Text[at - 1] != '\n')
            {
                at--;
            }

            Assert.Equal("[", chat.Text[at..(at + 1)]);
            Assert.False(BoldAt(chat, at), "the scaffolding took the message's formatting");
            Assert.Equal(TranscriptStyle.HeadlineColor, ColourAt(chat, at));
        });
    }

    [Fact]
    public void AFaceInsideAStyledRunIsStillAFace()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            // The face is drawn as a picture, and a picture is not a character the transcript can be searched for,
            // so the words before it are what the formatting is measured on.
            // A face is one character to anybody counting them: thirteen counted characters, not fourteen
            // UTF-16 units, or the runs do not add up and the wire throws the whole annotation away.
            Receive(service, Styled("User 2", "Room 1", "all of this \U0001F600",
                new StyledRun(13, Bold: true, Color: "#0000C0")));

            var pictures = Rtf(form).Split(@"\pict").Length - 1;

            Assert.Equal(1, pictures);
            Assert.True(BoldAt(chat, BodyIndex(chat, "all of this")), "the run stopped at the face");
        });
    }

    [Fact]
    public void ALargeFaceIsDrawnLargerThanAnOrdinaryOne()
    {
        Sta.Run(() =>
        {
            // A face in the transcript is a picture rather than a glyph, which means its size is decided here and
            // not by the font: an outline glyph grows with the run it is in because the font does the growing, and a
            // PNG grows only if whoever wrote it asked. A Large face that arrives the ordinary size is a message
            // that stopped saying what it said on the way to the room.
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            Receive(service, Styled("User 2", "Room 1", $"shout {Face}",
                new StyledRun(6), new StyledRun(1, Size: StyledRun.Large)));
            Receive(service, new TextMessage("User 2", "Room 1", $"shout {Face}"));

            var faces = Pictures(chat);

            Assert.Equal(2, faces.Count);

            // The ink rather than the picture, because the ink is the face: a picture is a box the face is stood in,
            // and the box is the height of the line.
            Assert.True(faces[0].Ink.Height > faces[1].Ink.Height,
                $"the large face inks {faces[0].Ink.Height} pixels and the ordinary one {faces[1].Ink.Height}");
        });
    }

    [Fact]
    public void ThreeFacesTogetherAreThreePicturesAndNotOne()
    {
        Sta.Run(() =>
        {
            // Faces that arrive together are one stretch of text, and one stretch was written into the transcript
            // as one RTF string - which meant one RTF *document* per face, and three documents where one was
            // wanted. RichEdit reads a document and stops; what it does with the second and third is not put the
            // other two faces on the screen. Somebody who sent three faces got one of them, and could not tell
            // from the transcript which of their three it was.
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            Receive(service, new TextMessage("User 2", "Room 1", "🙂😀👍"));

            Assert.Equal(3, PictureHeights(form).Count);

            // And three faces are three different pictures. Two faces coming out of the transcript with the same
            // artwork is the other half of the report - the faces that arrived were not necessarily the ones sent.
            var pictures = PictureBodies(chat);

            Assert.Equal(3, pictures.Count);
            Assert.Equal(3, pictures.Distinct().Count());
        });
    }

    [Fact]
    public void AFaceStandsOnTheLineItsWordsAreOn()
    {
        Sta.Run(() =>
        {
            // "The emoji icon and the text vertical not aligned." A face in the transcript is a picture, and a
            // picture goes where the control puts it - which, measured on six lines, is the same place whatever else
            // is on them: the top of the line. The ink of a face sits 3 pixels in from the top of the picture it
            // came in, so a face has always ended with its chin 15 pixels down, whether the line it stands among is
            // 14, 18 or 25 pixels tall. The letters of those lines stop at 13, 16 and 23: two pixels above the bottom
            // of the line's own height at the room's size and at Large, and one at Small. So a face beside Large
            // words hung eight pixels in the air, and a small face beside ordinary ones six, and nothing about the
            // message says which.
            //
            // The picture's top is the one thing about it the control will not move, so the picture is made as tall
            // as the tallest letters on the line and carries its ink two pixels above the bottom of that - which is
            // where those letters stop, at every size measured. The face is not stretched to fill the box: it is the
            // same picture of a face at the same size, standing lower. The face is not stretched to fill the box - the same face
            // beside the same words at two sizes is the same picture of a face, standing lower.
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);

            Receive(service, new TextMessage("User 2", "Room 1", $"words here {Face}"));
            Receive(service, new TextMessage("User 2", "Room 1", $"words here {Face}")
            {
                Styles = [new StyledRun(11, Size: StyledRun.Large), new StyledRun(1)]
            });

            var ordinary = new Font(chat.Font.FontFamily, chat.Font.Size);
            var large = new Font(chat.Font.FontFamily, chat.Font.Size + TranscriptStyle.LargeDelta);

            var faces = Pictures(chat);

            Assert.Equal(2, faces.Count);

            // Each face's picture is as tall as the tallest letters on its line, and its ink stands one pixel above
            // the bottom of that - which is the baseline the line is drawn on.
            Assert.Equal(ordinary.Height, faces[0].Told);
            Assert.Equal(large.Height, faces[1].Told);

            Assert.Equal(faces[0].Told - 1, faces[0].Ink.Bottom);
            Assert.Equal(faces[1].Told - 1, faces[1].Ink.Bottom);

            // And it is the same face both times. Standing on a taller line is not the same instruction as "a
            // bigger face": a face typed at the room's size stays that size whatever walks past it in Large.
            Assert.Equal(faces[0].Ink.Size, faces[1].Ink.Size);
        });
    }

    [Fact]
    public void ASmallFaceIsDrawnSmallerThanAnOrdinaryOne()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);

            EnterRoom(form);
            Receive(service, Styled("User 2", "Room 1", $"quiet {Face}",
                new StyledRun(6), new StyledRun(1, Size: StyledRun.Small)));
            Receive(service, new TextMessage("User 2", "Room 1", $"quiet {Face}"));

            var faces = Pictures(chat);

            Assert.Equal(2, faces.Count);

            // The ink, not the picture. A picture is now as tall as the letters it stands among - which these two
            // lines have in common, the face being the only thing typed Small on one of them - so what is smaller
            // about a small face is the face.
            Assert.True(faces[0].Ink.Height < faces[1].Ink.Height,
                $"the small face inks {faces[0].Ink.Height} pixels and the ordinary one {faces[1].Ink.Height}");

            // And both stand on the bottom of their box, which is where the line's letters stop.
            Assert.Equal(faces[0].Told - 1, faces[0].Ink.Bottom);
            Assert.Equal(faces[1].Told - 1, faces[1].Ink.Bottom);
        });
    }

    [Fact]
    public void AStyledLineComesBackStyledWhenTheRoomIsReplayed()
    {
        Sta.Run(() =>
        {
            var service = new FakeChatService("Haitao");
            using var form = new Lantern.UI.HostForm(service);
            var chat = Chat(form);
            var rooms = (ListBox)Find(form, "lstRooms")!;

            EnterRoom(form);
            Receive(service, Styled("User 2", "Room 1", "keep this bold",
                new StyledRun(10, Bold: true), new StyledRun(4)));

            rooms.SelectedIndex = 1;
            rooms.SelectedIndex = 0;

            Assert.True(BoldAt(chat, BodyIndex(chat, "keep this bold")),
                "history replayed the line but threw the formatting away");
        });
    }

    [Fact]
    public void EveryWindowDrawsAStyledLineTheSameWay()
    {
        // Not "the sender's window" specifically - a send is never echoed back by the service, and the sender's
        // line is drawn by the same code as anybody else's. What has to be true is that the two flavours of
        // window, given one message, put the same ink on the screen.
        Sta.Run(() =>
        {
            var message = Styled("User 2", "Room 1", "what was typed",
                new StyledRun(4, Bold: true), new StyledRun(10));

            var host = new FakeChatService("Haitao");
            using var hostForm = new Lantern.UI.HostForm(host);
            EnterRoom(hostForm);
            host.SimulateIncoming(message);

            var client = new FakeChatService("Haitao", isHost: false);
            using var clientForm = new Lantern.UI.ClientForm(client);
            EnterRoom(clientForm);
            client.SimulateIncoming(message);

            var hostChat = Chat(hostForm);
            var clientChat = Chat(clientForm);

            Assert.True(BoldAt(hostChat, BodyIndex(hostChat, "what was typed")), "the host window lost it");
            Assert.True(BoldAt(clientChat, BodyIndex(clientChat, "what was typed")), "the client window lost it");
            Assert.False(BoldAt(hostChat, BodyIndex(hostChat, "what was typed") + 5));
            Assert.False(BoldAt(clientChat, BodyIndex(clientChat, "what was typed") + 5));
        });
    }

    // ----------------------------------------------------------------------------------------- domain

    [Fact]
    public void RunsAreCutWhereTheRulerCountsThem()
    {
        const string Body = "\U0001F600\U0001F600A\U0001F600\U0001F600 is a family";

        var pieces = StyledRun.Split(Body, [new StyledRun(3), new StyledRun(2, Bold: true), new StyledRun(12)]);

        // Four faces and an A are five counted characters, so the first run stops after the A and the second
        // takes exactly the two faces that follow. Cutting by UTF-16 units would have stopped mid-face.
        Assert.Equal(3, pieces.Count);
        Assert.Equal("\U0001F600\U0001F600A", pieces[0].Text);
        Assert.Equal("\U0001F600\U0001F600", pieces[1].Text);
        Assert.Equal(" is a family", pieces[2].Text);
        Assert.False(pieces[0].Run.Bold);
        Assert.True(pieces[1].Run.Bold);
    }

    [Fact]
    public void CuttingAlwaysGivesBackTheSentence()
    {
        foreach (var body in new[] { "", "plain", "\U0001F468\U0001F469\U0001F467 family", "two\nlines" })
        {
            foreach (var runs in new[]
            {
                Array.Empty<StyledRun>(),
                [new StyledRun(1)],
                [new StyledRun(2, Bold: true), new StyledRun(40, Italic: true)],
                [new StyledRun(0), new StyledRun(3)],
                [new StyledRun(100)],
            })
            {
                var joined = new StringBuilder();

                foreach (var piece in StyledRun.Split(body, runs))
                {
                    joined.Append(piece.Text);
                }

                Assert.Equal(body, joined.ToString());
            }
        }
    }

    [Fact]
    public void NoPieceEverEndsInsideAnEmoji()
    {
        const string Body = "\U0001F468\U0001F469\U0001F467 a family \U0001F600";

        foreach (var cut in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 })
        {
            foreach (var piece in StyledRun.Split(Body, [new StyledRun(cut)]))
            {
                var last = piece.Text.Length == 0 ? '\0' : piece.Text[^1];

                Assert.False(char.IsHighSurrogate(last),
                    $"a cut of {cut} stopped between the two halves of a face");
            }
        }
    }

    [Fact]
    public void ARunWithNoStyleIsStillARun()
    {
        var pieces = StyledRun.Split("all of it", [new StyledRun(9)]);

        Assert.Single(pieces);
        Assert.Equal("all of it", pieces[0].Text);
        Assert.Equal(new StyledRun(9), pieces[0].Run);
    }

    [Fact]
    public void APlainMessageSplitsIntoOnePiece()
    {
        var pieces = StyledRun.Split("nothing said about styling", []);

        Assert.Single(pieces);
        Assert.Equal("nothing said about styling", pieces[0].Text);
    }

    // ------------------------------------------------------------------------------------------ helpers

    /// <summary>
    /// A text message with these runs on it - and a complaint before it is sent if the runs do not add up.
    /// <para>
    /// A run list that does not tile the sentence is refused (T12.1), so a fixture that got its arithmetic wrong
    /// would not fail as "the runs were wrong", it would quietly arrive plain and then fail as "the transcript is
    /// broken" - which is a long way to go to diagnose an addition.
    /// </para>
    /// </summary>
    private static TextMessage Styled(string from, string room, string body, params StyledRun[] runs)
    {
        Assert.True(StyledRun.Fits(runs, body),
            $"the runs on \"{body}\" cover {runs.Sum(r => r.Length)} of the {ValidationRules.Length(body)} characters it has");

        return new TextMessage(from, room, body) { Styles = runs };
    }

    private static RichTextBox Chat(Form form) => (RichTextBox)Find(form, "rtbChat")!;

    private static string Rtf(Form form)
    {
        var chat = Chat(form);

        chat.SelectAll();

        return chat.SelectedRtf;
    }

    /// <summary>
    /// How tall each face the transcript drew actually is, in the order they were written. <see cref="RtfPicture"/>
    /// states a picture's size in device pixels, so the transcript's own RTF says what was asked for.
    /// </summary>
    /// <summary>
    /// The artwork of each picture in the transcript, as the control wrote it back out. Enough of a picture to
    /// compare two of them with: if two faces have the same body, the same face was drawn twice.
    /// </summary>
    private static List<string> PictureBodies(RichTextBox chat)
    {
        chat.SelectAll();

        // The control writes a picture's bytes back out in lines, so the hex of one face is several runs with line
        // breaks between them rather than one: a picture is known by its hex end to end, breaks taken back out.
        return Regex.Matches(chat.SelectedRtf, @"pichgoal\d+[0-9a-f]+(?:\s*[0-9a-f]+)*")
            .Select(m => Regex.Replace(m.Value, @"\s", ""))
            .ToList();
    }

    /// <summary>
    /// Each picture the transcript holds: how tall it told RichEdit to be, in device pixels, and the bounds of the
    /// ink inside it. The height comes out of the RTF because that is what the control is told; the ink is measured
    /// on the decoded artwork, because the RTF says nothing about where inside a picture the face actually is.
    /// </summary>
    private static List<(int Told, Rectangle Ink)> Pictures(RichTextBox chat)
    {
        chat.SelectAll();

        var seen = new List<(int, Rectangle)>();

        foreach (Match picture in Regex.Matches(
            chat.SelectedRtf, @"pichgoal(\d+)((?:\s*[0-9a-f]+)*)"))
        {
            var png = Convert.FromHexString(Regex.Replace(picture.Groups[2].Value, @"\s", ""));

            using var stream = new MemoryStream(png);
            using var image = Image.FromStream(stream);

            // The size is told in twips, which is a hundred andfortieth of an inch: divided by fifteen it is the
            // device pixels the picture was asked for, and that is the domain the line's own heights are in.
            seen.Add((int.Parse(picture.Groups[1].Value) / 15, InkOf((Bitmap)image)));
        }

        return seen;
    }

    /// <summary>Everything in a picture that is not transparent, antialiasing included.</summary>
    private static Rectangle InkOf(Bitmap image)
    {
        var left = image.Width;
        var top = image.Height;
        var right = -1;
        var bottom = -1;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).A > 0)
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    private static List<int> PictureHeights(Form form)
    {
        var heights = new List<int>();

        foreach (Match picture in Regex.Matches(Rtf(form), @"\\pich(\d+)"))
        {
            heights.Add(int.Parse(picture.Groups[1].Value));
        }

        return heights;
    }

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

    private static void EnterRoom(Form form) => ((ListBox)Find(form, "lstRooms")!).SelectedIndex = 0;

    private static void Receive(FakeChatService service, Message message)
        => service.SimulateIncoming(message);

    /// <summary>Where a body starts in the transcript. The headline is above it and is never styled.</summary>
    private static int BodyIndex(RichTextBox chat, string body)
    {
        var at = chat.Text.IndexOf(body, StringComparison.Ordinal);

        Assert.True(at >= 0, $"the transcript never showed \"{body}\"");

        return at;
    }

    private static FontStyle StyleAt(RichTextBox chat, int index)
    {
        chat.Select(index, 1);

        return chat.SelectionFont?.Style ?? FontStyle.Regular;
    }

    private static bool BoldAt(RichTextBox chat, int index) => StyleAt(chat, index).HasFlag(FontStyle.Bold);

    private static float SizeAt(RichTextBox chat, int index)
    {
        chat.Select(index, 1);

        return chat.SelectionFont?.Size ?? 0f;
    }

    private static Color ColourAt(RichTextBox chat, int index)
    {
        chat.Select(index, 1);

        return chat.SelectionColor;
    }
}
