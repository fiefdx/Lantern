using Lantern.Core;
using Lantern.Domain;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T5.2: a picture across the wire (F7). A photo is the first message in Lantern whose body is not text,
/// so everything here is about what that changes: the bytes arriving exactly as they left, a photo that is
/// too big being refused in words rather than as a stream that has come out of step, and a peer still not
/// being believed about whose picture it is, what it is called, or which room it belongs to.
/// </summary>
public sealed class PictureMessageTests
{
    [Fact]
    public async Task APhotoArrivesWithEveryByteItLeftWith()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");
        var hostFrames = WatchFrames(host);

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var seenByCara = WatchMessages(cara);
        var seenByDov = WatchMessages(dov);

        var bytes = SmallPng();
        var sent = new PictureMessage("Cara", "Room 1", "sunset.png", bytes);
        await cara.SendAsync(sent);

        var arrived = (PictureMessage)await seenByDov.ItemAsync(m => m is PictureMessage, "Dov to see the photo");

        // The frame body is the picture, not base64 of it, so the two arrays have to be equal element by
        // element: one lost byte is a photo with a grey band across it.
        Assert.Equal(bytes, arrived.Data);
        Assert.Equal(bytes.LongLength, arrived.SizeBytes);
        Assert.Equal("sunset.png", arrived.FileName);
        Assert.Equal("Cara", arrived.SenderNickname);
        Assert.Equal("Room 1", arrived.RoomName);

        // The id is the sender's, so every window in the room is looking at one message rather than
        // three that happen to look alike, and a frame that arrives twice can be recognised as such.
        Assert.Equal(sent.Id, arrived.Id);
        Assert.Equal(sent.SentAt, arrived.SentAt);

        Assert.Contains("sunset.png", arrived.Preview);

        // The host's own window is a room member like any other, so it is given the same frame.
        await hostFrames.ItemAsync(f => f.Type == FrameType.PICTURE, "the host to be told about the photo");

        // ... and nobody hears their own picture come back.
        await Task.Delay(150);
        Assert.Equal(0, seenByCara.CountMatch(m => m is PictureMessage));
    }

    [Fact]
    public async Task TheHostsOwnPhotoReachesTheRoom()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");

        var seenByDov = WatchMessages(dov);

        var sent = server.SendPicture(host, "map.png", SmallPng());

        Assert.NotNull(sent);
        var arrived = (PictureMessage)await seenByDov.ItemAsync(m => m is PictureMessage, "Dov to see the host's photo");
        Assert.Equal("map.png", arrived.FileName);

        // The host's window is a participant like any other, which is also why its pictures carry the
        // host's nickname rather than whatever the caller put in the message.
        Assert.Equal("Hosty", arrived.SenderNickname);
        Assert.Equal(sent!.Id, arrived.Id);
    }

    [Fact]
    public async Task APhotoSentTwiceIsTwoPhotosWithTwoIdentities()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var seenByDov = WatchMessages(dov);

        // Same file name, same bytes: the only thing that tells these apart is the id, so if the server
        // ever minted its own the transcript could not tell a re-send from a second copy.
        var first = new PictureMessage("Cara", "Room 1", "same.png", SmallPng());
        var second = new PictureMessage("Cara", "Room 1", "same.png", first.Data);

        await cara.SendAsync(first);
        await cara.SendAsync(second);

        await seenByDov.UntilAsync(items => items.OfType<PictureMessage>().Count() == 2, "both photos");

        var arrived = seenByDov.Items.OfType<PictureMessage>().ToArray();
        Assert.Equal([first.Id, second.Id], arrived.Select(m => m.Id));
        Assert.DoesNotContain(Guid.Empty, arrived.Select(m => m.Id));
    }

    [Fact]
    public async Task APhotoTooBigForOneFrameIsRefusedInWordsAndNobodyIsGivenIt()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [dov], "Room 1");
        var seenByDov = WatchMessages(dov);

        // The client refuses this itself before writing (see APhotoTooBigIsRefusedOnTheSendingSide), but
        // the host is the one that decides: a peer built without that check must not be able to make
        // every machine in the room hold a picture bigger than a frame.
        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Mallory"), Frame.JoinRoom(server.Id("Room 1")));
        await SendRawAsync(raw, Frame.Picture(Guid.NewGuid(), server.RoomOrThrow("Room 1"), "Mallory", "huge.png", new byte[(int)ValidationRules.MaxPictureBytes + 1], DateTime.Now));

        var refusal = await ReadRawAsync(raw, FrameType.ERROR);
        Assert.Contains("MB", refusal.Header.Error, StringComparison.Ordinal);
        Assert.Contains("5", refusal.Header.Error, StringComparison.Ordinal);

        await Task.Delay(150);
        Assert.Equal(0, seenByDov.CountMatch(m => m is PictureMessage));

        // The connection survives the refusal: a refused picture is a refusal, not a reason to drop the
        // person. If the oversized frame had been written to the stream regardless, everything after this
        // would come back as a corrupt-frame error instead.
        await SendRawAsync(raw, Frame.Text(Guid.NewGuid(), server.RoomOrThrow("Room 1"), "Mallory", "still here?", DateTime.Now));
        var heard = (TextMessage)await seenByDov.ItemAsync(m => m is TextMessage, "the sentence after the refusal");
        Assert.Equal("still here?", heard.Body);
    }

    [Fact]
    public async Task APhotoTooBigIsRefusedOnTheSendingSide()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var seenByCara = WatchMessages(cara);
        var seenByDov = WatchMessages(dov);
        var problems = new Watch<string>();
        cara.ServerError += (_, text) => problems.Add(text);

        var tooBig = new PictureMessage("Cara", "Room 1", "huge.png", new byte[(int)ValidationRules.MaxPictureBytes + 1]);

        // Said here, in this process, in the same words the host would have used: the window that offered
        // the file can put that in its status bar instead of waiting a round trip to be told.
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => cara.SendAsync(tooBig));
        Assert.Contains("5 MB", refusal.Message, StringComparison.Ordinal);

        await Task.Delay(150);
        Assert.Equal(0, seenByDov.CountMatch(m => m is PictureMessage));

        // Nothing was written, so the link is still a link and the transcript still holds only what went.
        Assert.Empty(problems.Items);
        Assert.Equal(0, seenByCara.CountMatch(m => m is PictureMessage));

        await cara.SendAsync(new TextMessage("Cara", "Room 1", "found a smaller one"));
        await seenByDov.ItemAsync(m => m is TextMessage, "the next message on the same connection");
    }

    [Fact]
    public async Task AnEmptyPhotoIsRefusedRatherThanShownAsABlankSpace()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Mallory"), Frame.JoinRoom(server.Id("Room 1")));
        await SendRawAsync(raw, Frame.Picture(Guid.NewGuid(), server.RoomOrThrow("Room 1"), "Mallory", "blank.png", [], DateTime.Now));

        var refusal = await ReadRawAsync(raw, FrameType.ERROR);
        Assert.Contains("empty", refusal.Header.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task APhotoBeforeARoomIsRefused()
    {
        await using var server = await StartAsync();

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Mallory"));
        await SendRawAsync(raw, Frame.Picture(Guid.NewGuid(), server.RoomOrThrow("Room 1"), "Mallory", "sunset.png", SmallPng(), DateTime.Now));

        var refusal = await ReadRawAsync(raw, FrameType.ERROR);
        Assert.Contains("room", refusal.Header.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AFrameClaimingAnotherPersonAndAnotherRoomIsBelievedAboutNeither()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var dov = new ChatClient("Dov");
        await using var ines = new ChatClient("Ines");
        await JoinAllAsync(server, [dov, ines], "Room 1");

        var seenByDov = WatchMessages(dov);
        var seenByInes = WatchMessages(ines);

        using var raw = await new BtLoopbackClient().ConnectAsync(server.Endpoint);
        await SendRawAsync(raw, Frame.Hello("Cara"), Frame.JoinRoom(server.Id("Room 1")));

        // Both the name and the room in this frame are lies: Cara is not Dov, and Cara is standing in
        // Room 1. The server answers for both, exactly as it does for a text message (T2.4).
        await SendRawAsync(raw, Frame.Picture(Guid.NewGuid(), server.RoomOrThrow("Room 2"), "Dov", "mislabelled.png", SmallPng(), DateTime.Now));

        var arrived = (PictureMessage)await seenByDov.ItemAsync(m => m is PictureMessage, "the photo to reach the room");
        Assert.Equal("Cara", arrived.SenderNickname);
        Assert.Equal("Room 1", arrived.RoomName);

        await seenByInes.ItemAsync(m => m is PictureMessage, "the other member to see it too");

        // Nobody was moved into Room 2 by a frame that said so.
        Assert.Equal("Room 1", dov.CurrentRoomName);
        Assert.Equal(0, seenByInes.CountMatch(m => m.RoomName == "Room 2"));
    }

    [Fact]
    public async Task APeersPathIsStrippedToAFileNameBeforeAnyoneSeesIt()
    {
        await using var server = await StartAsync();
        var host = await HostJoinsAsync(server, "Room 1");

        // A name is a name. Whatever a peer sends - its own absolute path, or something aimed at this
        // machine's folders - reaches the room as a last component, because the transcript shows this
        // text and a save dialog is offered it later.
        Assert.Equal("cat.png", server.SendPicture(host, @"C:\Users\haitao\Pictures\cat.png", SmallPng())!.FileName);
        Assert.Equal("sam", server.SendPicture(host, @"..\..\windows\system32\config\sam", SmallPng())!.FileName);

        // A name that is nothing at all is still a picture that arrived, so it gets a word instead of a
        // blank in the transcript.
        Assert.Equal("image", server.SendPicture(host, @"C:\Users\haitao\Pictures\", SmallPng())!.FileName);
    }

    [Fact]
    public async Task LeavingAndComingBackKeepsThePhotoInTheRoomsHistory()
    {
        await using var server = await StartAsync();
        await HostJoinsAsync(server, "Room 1");

        await using var cara = new ChatClient("Cara");
        await using var dov = new ChatClient("Dov");
        await JoinAllAsync(server, [cara, dov], "Room 1");

        var bytes = SmallPng();
        var seenByDov = WatchMessages(dov);
        await cara.SendAsync(new PictureMessage("Cara", "Room 1", "keeps.png", bytes));
        await seenByDov.ItemAsync(m => m is PictureMessage, "the photo to arrive");

        await dov.JoinByNameWhenShownAsync("Room 2");
        await Watch.UntilAsync(() => dov.CurrentRoomName == "Room 2", "Dov to be in Room 2");
        await dov.JoinByNameWhenShownAsync("Room 1");
        await Watch.UntilAsync(() => dov.CurrentRoomName == "Room 1", "Dov to be back");

        // F6 replays a room's history when it is entered again, and a photo is part of that history: the
        // bytes live in the transcript, not only in the window that happened to be open.
        var history = RoomByName(dov, "Room 1").Transcript.OfType<PictureMessage>().ToArray();
        var kept = Assert.Single(history);
        Assert.Equal("keeps.png", kept.FileName);
        Assert.Equal(bytes, kept.Data);
    }

    [Fact]
    public void APictureKnowsItsOwnThumbnailSize()
    {
        // The transcript draws a thumbnail, so the number of pixels drawn must not depend on how big the
        // photo was: a phone photo is 4000 pixels wide and the window is not.
        Assert.Equal(160, PictureMessage.ThumbnailEdge);

        // A phone-sized photo, exactly two pixels wide to every pixel tall, so the expected thumbnail is
        // exact and the test is about the scaling rather than about rounding.
        using var big = new Bitmap(PictureMessage.ThumbnailEdge * 2, PictureMessage.ThumbnailEdge);
        using var stream = new MemoryStream();
        big.Save(stream, System.Drawing.Imaging.ImageFormat.Png);

        var message = new PictureMessage("Cara", "Room 1", "big.png", stream.ToArray());
        using var thumbnail = message.ToThumbnail();

        Assert.NotNull(thumbnail);
        Assert.Equal(PictureMessage.ThumbnailEdge, Math.Max(thumbnail!.Width, thumbnail.Height));
        Assert.Equal(PictureMessage.ThumbnailEdge, thumbnail.Width);
        Assert.Equal(PictureMessage.ThumbnailEdge / 2, thumbnail.Height);
    }

    [Fact]
    public void AThumbnailOfSomethingThatIsNotAnImageIsNothingRatherThanAnException()
    {
        // GDI+ reports a bad payload by throwing, and the transcript must still show the line: the person
        // sent something, and "[image] notes.txt" is the truth about it.
        var message = new PictureMessage("Cara", "Room 1", "notes.txt", System.Text.Encoding.UTF8.GetBytes("not a picture at all"));

        Assert.Null(message.ToThumbnail());
        Assert.Contains("notes.txt", message.Preview);
    }

    // ---------------------------------------------------------------- plumbing

    private static async Task<ChatServer> StartAsync()
    {
        var server = new ChatServer(new BtLoopbackServer("Test Host"), "Test Host");
        await server.StartAsync();
        return server;
    }

    private static async Task<LocalParticipant> HostJoinsAsync(ChatServer server, string roomName)
    {
        var local = new LocalParticipant("Hosty");
        Assert.True(server.Register(local));
        server.JoinByName(local, roomName);
        await Task.CompletedTask;
        return local;
    }

    private static async Task JoinAllAsync(ChatServer server, IEnumerable<ChatClient> clients, string roomName)
    {
        foreach (var client in clients)
        {
            await client.ConnectAsync(server.Endpoint);
            await client.JoinByNameWhenShownAsync(roomName);
        }

        await Watch.UntilAsync(
            () => clients.All(c => c.CurrentRoomName == roomName),
            $"the joins to reach {roomName}");
    }

    private static async Task SendRawAsync(IClientLink link, params Frame[] frames)
    {
        foreach (var frame in frames)
        {
            await FrameCodec.WriteAsync(link.Output, frame);
        }

        await link.Output.FlushAsync();
    }

    private static async Task<Frame> ReadRawAsync(IClientLink link, FrameType wanted)
    {
        using var cancel = new CancellationTokenSource(Watch.DefaultTimeoutMs);

        while (true)
        {
            var frame = await FrameCodec.ReadAsync(link.Input, cancel.Token);

            if (frame is null)
            {
                throw new TimeoutException($"the connection ended before a {wanted} frame arrived");
            }

            if (frame.Type == wanted)
            {
                return frame;
            }
        }
    }

    [Fact]
    public void APictureDecodesAtItsOwnSizeForLookingAt()
    {
        // A thumbnail caps an edge at 160 pixels because it sits in a line of text. The window a photo opens
        // in (F14) shows the photo, so it asks for the other thing: the picture at its own size.
        var message = new PictureMessage("User 2", "Room 1", "small.png", SmallPng());

        using var image = message.ToImage();

        Assert.NotNull(image);
        Assert.Equal(4, image!.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void TheDecodedPictureOutLivesTheStreamItCameFrom()
    {
        // Image.FromStream keeps reading from its stream while the image is drawn, so an image handed out over
        // a stream that has since been closed is a bomb with a delay - and a photo window is exactly the thing
        // that draws it later, on a repaint, when the message that carried it is long gone. Drawing it is the
        // only way to know the copy really is a copy.
        var message = new PictureMessage("User 2", "Room 1", "small.png", SmallPng());

        using var image = message.ToImage();

        Assert.NotNull(image);

        using (var graphics = Graphics.FromImage(image!))
        {
            graphics.FillRectangle(Brushes.Red, 0, 0, image!.Width, image.Height);
        }

        // And bytes that are not a picture are no picture rather than an exception, so the transcript keeps its
        // line and the window that was asked to show them has something true to say.
        Assert.Null(new PictureMessage("User 2", "Room 1", "notes.txt", System.Text.Encoding.UTF8.GetBytes("a shopping list")).ToImage());
    }

    private static Watch<Frame> WatchFrames(LocalParticipant participant)
    {
        var watch = new Watch<Frame>();
        participant.FrameReceived += (_, frame) => watch.Add(frame);
        return watch;
    }

    private static Watch<Message> WatchMessages(ChatClient client)
    {
        var watch = new Watch<Message>();
        client.MessageReceived += (_, message) => watch.Add(message);
        return watch;
    }

    private static Room RoomByName(ChatClient client, string name)
        => client.Rooms.Single(r => r.Name == name);

    /// <summary>
    /// A real 4x2 PNG. Made here rather than written as bytes in the test because the point of the test is
    /// that the bytes that arrive are a picture the operating system can still decode.
    /// </summary>
    private static byte[] SmallPng()
    {
        using var bitmap = new Bitmap(4, 2);
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
}
