using System.Text;
using Lantern.Core;
using Lantern.Domain;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T12.1: formatting that travels <em>beside</em> the sentence instead of inside it (v1.7).
/// <para>
/// The whole design hangs on one rule — the sentence is never optional. <see cref="TextMessage.Body"/>
/// stays plain text, and the runs laid over it are an annotation any receiver may throw away. There is
/// one failure mode, a style list that disagrees with its own text, and one remedy: read the sentence.
/// Everything below is that rule, the ruler the runs are counted with, or the thing a protocol change can
/// never break — a peer that has never heard of this field still getting the message it would have got
/// last week.
/// </para>
/// </summary>
public class StyledTextTests
{
    private static readonly Guid WireRoomId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTime SentAt = new(2026, 1, 2, 3, 4, 5);

    private static Room WireRoom(string name = "Room 1") => new(WireRoomId, name);

    // ------------------------------------------------------------------- the rule

    /// <summary>
    /// The assertion that says a v1.6 peer is not being subtly broken: the same words with formatting
    /// bolted on arrive as the same words, byte for byte — and a message nobody formatted says nothing
    /// whatever about formatting.
    /// </summary>
    [Fact]
    public void AStyledMessageStillCarriesTheSentenceByteForByte()
    {
        // "see the " then "bold" then " word": seventeen characters, and seventeen is what the runs have to
        // add up to. (The design document's own example says 10 + 4 + 5 over this sentence, which is two too
        // many — the rule in the very next bullet of that document is the one that refuses it.)
        const string Body = "see the bold word";

        IReadOnlyList<StyledRun> runs = [new StyledRun(8), new StyledRun(4, Bold: true), new StyledRun(5)];

        var id = Guid.NewGuid();
        var plain = Frame.Text(id, WireRoom(), "Haitao", Body, SentAt);
        var styled = Frame.Text(id, WireRoom(), "Haitao", Body, SentAt, runs);

        // Nothing said about formatting is nothing sent about it: no field, and not an empty one either.
        Assert.DoesNotContain("styles", plain.JsonHeader);

        // And when something is said, it is the shape the design document says it is: run lengths keyed
        // by "n", the style flags beside them, and no restatement of the sentence anywhere.
        Assert.Contains("""[{"n":8},{"n":4,"b":true},{"n":5}]""", styled.JsonHeader);

        var plainLine = Received(plain);
        var styledLine = Received(styled);

        Assert.Equal(Body, plainLine.Body);
        Assert.Equal(Body, styledLine.Body);
        Assert.Equal(Encoding.UTF8.GetBytes(plainLine.Body), Encoding.UTF8.GetBytes(styledLine.Body));

        Assert.Empty(plainLine.Styles);
        Assert.Equal(runs, styledLine.Styles);
    }

    /// <summary>
    /// A style list that disagrees with the text is a bug in the sender, and the recipient has no way to
    /// guess which half to believe — so it believes the sentence and throws the list away. Never half of
    /// it either: part of a list applied to a whole sentence is how one bold word turns up on three.
    /// </summary>
    [Fact]
    public void ARunThatDoesNotAddUpToTheMessageIsThrownAwayWhole()
    {
        const string Body = "four score and seven";      // 20 characters by the ruler

        var oneShort = new[] { new StyledRun(10), new StyledRun(9, Bold: true) };
        var oneLong = new[] { new StyledRun(10), new StyledRun(11, Bold: true) };
        var deadRun = new[] { new StyledRun(20), new StyledRun(0, Bold: true) };   // adds up, but claims a run of nothing

        foreach (var runs in new[] { oneShort, oneLong, deadRun })
        {
            var frame = Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", Body, SentAt, runs);

            // It did go out. It is the receiver that refuses it, not the sender quietly tidying up.
            Assert.Contains("styles", frame.JsonHeader);

            var line = Received(frame);

            Assert.Equal(Body, line.Body);
            Assert.Equal("four score and seven", line.Preview);
            Assert.Empty(line.Styles);
        }
    }

    /// <summary>
    /// Runs are counted in characters somebody counts, not in UTF-16 offsets — which is the difference
    /// between styling a family and styling half of one. A sender that measured with <c>string.Length</c>
    /// would send eight of what the ruler calls one, and lose every run in the message with it.
    /// </summary>
    [Fact]
    public void AFamilyEmojiIsOneCharacterOfStyleOrNone()
    {
        const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";

        Assert.Equal(8, Family.Length);                       // what the control stores
        Assert.Equal(1, ValidationRules.Length(Family));      // what somebody counts

        var styled = Received(Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", Family, SentAt, [new StyledRun(1, Bold: true)]));

        Assert.Equal(Family, styled.Body);
        Assert.Equal([new StyledRun(1, Bold: true)], styled.Styles);

        var countedInUnits = Received(Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", Family, SentAt, [new StyledRun(8, Bold: true)]));

        Assert.Equal(Family, countedInUnits.Body);
        Assert.Empty(countedInUnits.Styles);
    }

    /// <summary>
    /// More than sixty-four runs is either nonsense or somebody trying to make a GDI control spend its
    /// evening laying out runs. The cap refuses the list; the message is still a message.
    /// </summary>
    [Fact]
    public void SixtyFiveRunsOfStyleDoNotArrive()
    {
        var body = new string('x', StyledRun.MaxRuns + 1);
        var tooMany = Enumerable.Range(0, StyledRun.MaxRuns + 1).Select(_ => new StyledRun(1, Bold: true)).ToArray();

        var refused = Received(Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", body, SentAt, tooMany));

        Assert.Equal(body, refused.Body);
        Assert.Empty(refused.Styles);

        // The cap is exactly the cap: sixty-four runs of one character are a formatted message.
        var exactly = new string('x', StyledRun.MaxRuns);
        var fits = Enumerable.Range(0, StyledRun.MaxRuns).Select(i => new StyledRun(1, Bold: i % 2 == 0)).ToArray();

        var kept = Received(Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", exactly, SentAt, fits));

        Assert.Equal(exactly, kept.Body);
        Assert.Equal(fits, kept.Styles);
    }

    /// <summary>
    /// Bold, italic and underline are three independent flags on a run, not a font-style enum, so the
    /// RichTextBox's style-mask trap cannot be dressed up as a wire problem: three bits go out and three
    /// bits come back. Named, because a theory over three booleans is how a combination stops being read.
    /// </summary>
    [Fact]
    public void BoldItalicAndUnderlineOnOneCharacterComeBackAsAllThree()
    {
        IReadOnlyList<StyledRun> runs =
        [
            new StyledRun(1, Bold: true, Italic: true, Underline: true),
            new StyledRun(3),
        ];

        var line = Received(Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", "ship", SentAt, runs));

        Assert.Equal("ship", line.Body);
        Assert.Equal(runs, line.Styles);

        var first = line.Styles[0];
        Assert.True(first.Bold);
        Assert.True(first.Italic);
        Assert.True(first.Underline);
        Assert.Equal(StyledRun.Normal, first.Size);
        Assert.Null(first.Color);

        var second = line.Styles[1];
        Assert.False(second.Bold);
        Assert.False(second.Italic);
        Assert.False(second.Underline);
    }

    /// <summary>One flag alone leaves the other two off, so the mask is composed and not overwritten.</summary>
    [Fact]
    public void ItalicAloneLeavesBoldAndUnderlineAlone()
    {
        var line = Received(Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", "ship", SentAt, [new StyledRun(4, Italic: true)]));

        var only = Assert.Single(line.Styles);
        Assert.True(only.Italic);
        Assert.False(only.Bold);
        Assert.False(only.Underline);
    }

    /// <summary>Size and colour ride along, and "normal" is the absence of a size rather than a value.</summary>
    [Fact]
    public void ARunsSizeAndColourComeBackWithIt()
    {
        IReadOnlyList<StyledRun> runs =
        [
            new StyledRun(3, Size: StyledRun.Large, Color: "#C00000"),
            new StyledRun(4, Size: StyledRun.Small),
        ];

        var frame = Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", "big red", SentAt, runs);

        // A run with nothing to say about its size sends no size, exactly like a plain run sends no flags.
        Assert.Contains("""[{"n":3,"c":"#C00000","s":2},{"n":4,"s":0}]""", frame.JsonHeader);

        var line = Received(frame);

        Assert.Equal(runs, line.Styles);
        Assert.Equal(StyledRun.Large, line.Styles[0].Size);
        Assert.Equal("#C00000", line.Styles[0].Color);
        Assert.Equal(StyledRun.Small, line.Styles[1].Size);
        Assert.Null(line.Styles[1].Color);
    }

    /// <summary>A colour written in lower case is the same colour, and stays exactly as it was written.</summary>
    [Fact]
    public void AColourIsAColourHoweverItIsSpelled()
    {
        var frame = Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", "hello", SentAt, [new StyledRun(5, Color: "#c0ffee")]);

        var line = Received(frame);

        Assert.Equal([new StyledRun(5, Color: "#c0ffee")], line.Styles);
    }

    /// <summary>
    /// A size or a colour nobody can render is refused the way a run that does not add up is: the whole
    /// list goes and the sentence stays. Style fields are validated where they are accepted from the wire,
    /// never trusted because they arrived.
    /// </summary>
    [Fact]
    public void ASizeOrAcolourNobodyCouldRenderIsThrownAwayWithTheRest()
    {
        foreach (var json in new[]
        {
            """{"t":"TEXT","nick":"Haitao","room":"Room 1","text":"hello","styles":[{"n":5,"s":7}]}""",
            """{"t":"TEXT","nick":"Haitao","room":"Room 1","text":"hello","styles":[{"n":5,"s":-1}]}""",
            """{"t":"TEXT","nick":"Haitao","room":"Room 1","text":"hello","styles":[{"n":5,"c":"green"}]}""",
            """{"t":"TEXT","nick":"Haitao","room":"Room 1","text":"hello","styles":[{"n":5,"c":"#c0ffe"}]}""",
            """{"t":"TEXT","nick":"Haitao","room":"Room 1","text":"hello","styles":[{"n":5,"c":"c0ffee0"}]}""",
        })
        {
            var line = Assert.IsType<TextMessage>(new Frame(FrameHeader.FromJson(json)).ToMessage());

            Assert.Equal("hello", line.Body);
            Assert.Empty(line.Styles);
        }
    }

    /// <summary>
    /// The ruler a run is measured with, on its own: the list either covers the sentence exactly or it is
    /// not a description of it, and an empty list describes nothing.
    /// </summary>
    [Fact]
    public void RunsFitTheSentenceTheyAreLaidOverOrTheyDoNot()
    {
        const string Body = "five words";        // ten characters

        Assert.True(StyledRun.Fits([new StyledRun(4), new StyledRun(6, Bold: true)], Body));
        Assert.True(StyledRun.Fits(StyledRun.Plain(ValidationRules.Length(Body)), Body));

        Assert.False(StyledRun.Fits([new StyledRun(4), new StyledRun(5, Bold: true)], Body));
        Assert.False(StyledRun.Fits([new StyledRun(4), new StyledRun(7, Bold: true)], Body));
        Assert.False(StyledRun.Fits([new StyledRun(-6), new StyledRun(16)], Body));
        Assert.False(StyledRun.Fits([new StyledRun(0)], Body));
        Assert.False(StyledRun.Fits([], Body));
        Assert.False(StyledRun.Fits(null, Body));
        Assert.False(StyledRun.Fits([new StyledRun(10)], null));

        // And what is refused is refused whole, which is the only answer that cannot half-format a line.
        Assert.Empty(StyledRun.Accepted([new StyledRun(11)], Body));
        Assert.Equal([new StyledRun(10)], StyledRun.Accepted([new StyledRun(10)], Body));
        Assert.Empty(StyledRun.Accepted(null, Body));
    }

    // --------------------------------------------------------- the peer that knows nothing

    /// <summary>
    /// This is the v1.6 peer's defence, and the one test here that protects users rather than code: a
    /// header carrying a field nobody in this build has heard of still arrives as the message it arrived
    /// as — the same sender, the same room, the same words, and no formatting claimed anywhere.
    /// </summary>
    [Fact]
    public void AHeaderNobodyRecognisesStillArrivesAsTheMessageItArrivedAs()
    {
        var id = Guid.NewGuid();

        var json = $$"""{"t":"TEXT","id":"{{id}}","nick":"Haitao","room":"Room 1","text":"hello anyway","at":"2026-01-02T03:04:05","hats":3,"sizes":[1,2],"mood":"sunnily"}""";

        var line = Assert.IsType<TextMessage>(new Frame(FrameHeader.FromJson(json)).ToMessage());

        Assert.Equal(id, line.Id);
        Assert.Equal("Haitao", line.SenderNickname);
        Assert.Equal("Room 1", line.RoomName);
        Assert.Equal("hello anyway", line.Body);
        Assert.Empty(line.Styles);
        Assert.Equal(SentAt, line.SentAt);
    }

    /// <summary>
    /// The other half of growing a field: an unknown key <em>inside</em> a run is ignored and the run
    /// itself survives, so a later round can add strike-through without this round throwing away the bold
    /// sitting next to it.
    /// </summary>
    [Fact]
    public void AKeyNobodyHeardOfInsideARunLeavesTheRunItselfAlone()
    {
        var json = """{"t":"TEXT","nick":"Haitao","room":"Room 1","text":"hello","styles":[{"n":5,"b":true,"strike":true}]}""";

        var line = Assert.IsType<TextMessage>(new Frame(FrameHeader.FromJson(json)).ToMessage());

        Assert.Equal("hello", line.Body);
        Assert.Equal([new StyledRun(5, Bold: true)], line.Styles);
    }

    // ------------------------------------------------------------------ the fake service

    /// <summary>
    /// The windows run on <see cref="FakeChatService"/>, so a style list that survives the wire but not
    /// the fake would leave every UI test written later testing a service the product does not have.
    /// </summary>
    [Fact]
    public async Task TheFakeCarriesAStyledLineIntoTheRoomItRecordedItIn()
    {
        var service = new FakeChatService("Haitao");
        await service.JoinByNameAsync("Room 1");

        IReadOnlyList<StyledRun> runs = [new StyledRun(4, Bold: true), new StyledRun(11)];
        var message = new TextMessage("Haitao", "Room 1", "bold words here") { Styles = runs };

        await service.SendAsync(message);

        var sent = Assert.IsType<TextMessage>(service.Transcript.OfType<TextMessage>().Single(m => m.Id == message.Id));

        Assert.Equal("bold words here", sent.Body);
        Assert.Equal(runs, sent.Styles);

        var inTheRoom = Assert.IsType<TextMessage>(service.Rooms.Single(r => r.Name == "Room 1")
            .Transcript.OfType<TextMessage>().Single(m => m.Id == message.Id));

        Assert.Equal("bold words here", inTheRoom.Body);
        Assert.Equal(runs, inTheRoom.Styles);
    }

    /// <summary>
    /// ...and the fake refuses a list that does not add up in the same place the wire refuses it, because a
    /// double with kinder rules than the real thing is how a window comes to pass a test the room fails.
    /// </summary>
    [Fact]
    public async Task TheFakeThrowsAwayAStyleListThatDoesNotAddUp()
    {
        var service = new FakeChatService("Haitao");
        await service.JoinByNameAsync("Room 1");

        var message = new TextMessage("Haitao", "Room 1", "eight words") { Styles = [new StyledRun(99, Bold: true)] };

        await service.SendAsync(message);

        var sent = Assert.IsType<TextMessage>(service.Transcript.OfType<TextMessage>().Single(m => m.Id == message.Id));

        Assert.Equal("eight words", sent.Body);
        Assert.Equal(message.Id, sent.Id);
        Assert.Equal(message.SenderNickname, sent.SenderNickname);
        Assert.Empty(sent.Styles);
    }

    // ----------------------------------------------------------------------- machinery

    /// <summary>Encodes a frame, reads it back off the bytes, and rebuilds the transcript line it carries.</summary>
    private static TextMessage Received(Frame frame)
    {
        var decoder = new FrameDecoder();
        decoder.Append(FrameCodec.Encode(frame));

        var frames = decoder.ReadAllAvailable().ToArray();

        Assert.Single(frames);

        return Assert.IsType<TextMessage>(frames[0].ToMessage());
    }
}
