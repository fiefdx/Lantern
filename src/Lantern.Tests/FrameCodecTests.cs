using System.Runtime.CompilerServices;
using Lantern.Domain;
using Lantern.Transport;
using Xunit;

namespace Lantern.Tests;

/// <summary>
/// T2.2: the framing rules under the quirks a Bluetooth RFCOMM link actually produces — a frame
/// split across reads, two frames arriving glued together, an empty body, a large body, and a
/// stream that dribbles one byte out at a time. If these pass, the protocol cannot turn half a
/// picture into two chat messages.
/// </summary>
public class FrameCodecTests
{
    /// <summary>
    /// The room every frame in here is about. The codec has no idea what a room is and that is part of the
    /// point: an identity and a name go in, and the same identity and name come out, however the bytes were
    /// split on the way.
    /// </summary>
    private static readonly Guid WireRoomId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static Room WireRoom(string name = "Room 1") => new(WireRoomId, name);

    private static Frame Sample => Frame.Text(Guid.NewGuid(), WireRoom(), "Haitao", "hi", new DateTime(2026, 1, 2, 3, 4, 5));

    // ------------------------------------------------------------- round trip

    [Fact]
    public void EveryFrameTypeSurvivesARoundTrip()
    {
        var transfer = Guid.NewGuid();
        var id = Guid.NewGuid();
        var at = new DateTime(2026, 1, 2, 3, 4, 5);

        var frames = new[]
        {
            Frame.Hello("Haitao"),
            Frame.HelloAck("Haitao"),
            Frame.RoomList([WireRoom("Room 1"), WireRoom("Room 2")]),
            Frame.JoinRoom(WireRoomId),
            Frame.Text(id, WireRoom("Room 1"), "Haitao", "hi", at),
            Frame.Picture(id, WireRoom("Room 1"), "Haitao", "a.png", [1, 2, 3, 4], at),
            Frame.FileMeta(id, transfer, WireRoomId, "Room 1", "Haitao", "notes.txt", 4096),
            Frame.FileChunk(transfer, 7, [9, 8, 7]),
            Frame.FileDone(transfer),
            Frame.MemberList(WireRoomId, "Room 1", [new User("c1", "Haitao"), new User("c2", "Victory")]),
            Frame.SystemNotice(WireRoom("Room 1"), "Victory - join this chat"),
            Frame.Error("no such room"),
        };

        foreach (var frame in frames)
        {
            var decoded = DecodeSingle(FrameCodec.Encode(frame));

            Assert.Equal(frame.Type, decoded.Type);
            Assert.Equal(frame.JsonHeader, decoded.JsonHeader);
            Assert.Equal(frame.Body, decoded.Body);
        }
    }

    [Fact]
    public void AnEmptyBodyStaysEmpty()
    {
        var decoded = DecodeSingle(FrameCodec.Encode(Frame.JoinRoom(WireRoomId)));

        Assert.Empty(decoded.Body);
    }

    /// <summary>
    /// The one arithmetic that can quietly break a chat: bytes against characters.
    /// </summary>
    [Fact]
    public void AnEmojiTravelsAsFourBytesNotTwoCharacters()
    {
        const string Body = "cà phê \U0001F600\U0001F389";

        // The words of a message ride in the JSON header, and the length in front of a frame counts BYTES of that
        // UTF-8, because a byte count is the only thing a radio understands. One face is three different numbers:
        // two UTF-16 units, four bytes of UTF-8, and twelve characters once JSON has escaped it to 
        // "\ud83d\ude00". The framing has to use the middle one, and the arithmetic below is what says it did.
        Assert.Equal(4, System.Text.Encoding.UTF8.GetByteCount("\U0001F600"));
        Assert.Equal(17, System.Text.Encoding.UTF8.GetByteCount(Body));

        var frame = Frame.Text(Guid.NewGuid(), WireRoom(), "Xanh", Body, new DateTime(2026, 1, 2, 3, 4, 5));
        var onTheWire = FrameCodec.Encode(frame);

        // More bytes on the wire than characters in the sentence. Had anything counted characters, the frame would
        // be undersized: the far end would read part of a message and call whatever was left the beginning of the
        // next one.
        Assert.True(onTheWire.Length > System.Text.Encoding.UTF8.GetByteCount(Body),
            $"the frame is {onTheWire.Length} bytes for a 17 byte sentence");

        var heard = DecodeSingle(onTheWire).ToMessage();

        var text = Assert.IsType<TextMessage>(heard);
        Assert.Equal(Body, text.Body);
        Assert.Equal(9, ValidationRules.Length(text.Body));
    }

    [Fact]
    public void ABinaryBodyIsSentRawNotBase64()
    {
        // Base64 would inflate every picture by a third and cost a copy on both ends.
        var body = new byte[256];
        for (var i = 0; i < body.Length; i++)
        {
            body[i] = (byte)i;
        }

        var encoded = FrameCodec.Encode(Frame.Picture(Guid.NewGuid(), WireRoom("Room 1"), "Haitao", "a.bin", body, DateTime.Now));

        // The byte sequence itself must appear contiguously in what went on the wire.
        Assert.Contains(body, encoded);

        var decoded = DecodeSingle(encoded);
        Assert.Equal(body, decoded.Body);
        Assert.DoesNotContain("base64", decoded.JsonHeader);
    }

    [Fact]
    public void ALargeBodySurvives()
    {
        var body = new byte[700 * 1024];
        new Random(1234).NextBytes(body);

        var decoded = DecodeSingle(FrameCodec.Encode(Frame.Picture(Guid.NewGuid(), WireRoom("Room 1"), "Haitao", "big.png", body, DateTime.Now)));

        Assert.Equal(body.Length, decoded.Body.Length);
        Assert.Equal(body, decoded.Body);
    }

    [Fact]
    public void AMessageIdSurvivesTheWire()
    {
        var id = Guid.NewGuid();

        var decoded = DecodeSingle(FrameCodec.Encode(Frame.Text(id, WireRoom("Room 1"), "Haitao", "hi", DateTime.Now)));

        Assert.Equal(id, decoded.Header.Id);
    }

    /// <summary>
    /// A frame about a room says which room it means by identity, so the identity has to come out of the bytes
    /// intact. A name that arrived changed is a room renamed by a transmission; an identity that arrived
    /// changed is a message delivered into somebody else's room, which is the mistake the identity exists to
    /// stop.
    /// </summary>
    [Fact]
    public void ARoomsIdentitySurvivesTheWire()
    {
        var decoded = DecodeSingle(FrameCodec.Encode(Frame.JoinRoom(WireRoomId)));

        Assert.Equal(WireRoomId, decoded.Header.RoomId);
    }

    /// <summary>
    /// A room list carries both halves of a room: the identity, which is what the other machine uses from then
    /// on, and the name, which is what it prints. Without the first a window cannot act on a room; without the
    /// second it cannot say what it is looking at.
    /// </summary>
    [Fact]
    public void ARoomListCarriesTheIdentityAndTheName()
    {
        var study = Guid.NewGuid();
        var garden = Guid.NewGuid();

        var decoded = DecodeSingle(FrameCodec.Encode(Frame.RoomList([new Room(study, "Study"), new Room(garden, "Garden")])));

        Assert.Equal([study, garden], decoded.Header.Rooms!.Select(r => r.Id));
        Assert.Equal(["Study", "Garden"], decoded.Header.Rooms!.Select(r => r.Name));
    }

    /// <summary>Joining nothing - leaving - must not arrive as joining room zero.</summary>
    [Fact]
    public void LeavingARoomIsNotJoiningSomeOtherRoom()
    {
        var decoded = DecodeSingle(FrameCodec.Encode(Frame.JoinRoom(null)));

        Assert.Null(decoded.Header.RoomId);
    }

    // -------------------------------------------------- the stream-quirk tests

    [Fact]
    public void AFrameArrivingOneByteAtATimeIsStillOneFrame()
    {
        var bytes = FrameCodec.Encode(Sample);
        var decoder = new FrameDecoder();
        var frames = new List<Frame>();

        foreach (var b in bytes)
        {
            decoder.Append(stackalloc[] { b });
            frames.AddRange(decoder.ReadAllAvailable());
        }

        Assert.Single(frames);
        Assert.Equal("hi", frames[0].Header.Text);
        Assert.Equal(0, decoder.BufferedBytes);
    }

    [Fact]
    public void TwoFramesInOneReadBothComeOut()
    {
        var first = Frame.Text(Guid.NewGuid(), WireRoom("Room 1"), "Haitao", "one", DateTime.Now);
        var second = Frame.Text(Guid.NewGuid(), WireRoom("Room 1"), "Victory", "two", DateTime.Now);

        var decoder = new FrameDecoder();
        decoder.Append(FrameCodec.Encode(first).Concat(FrameCodec.Encode(second)).ToArray());

        var frames = decoder.ReadAllAvailable().ToArray();

        Assert.Equal(["one", "two"], frames.Select(f => f.Header.Text));
        Assert.Equal(0, decoder.BufferedBytes);
    }

    [Fact]
    public void NothingIsDecodedUntilTheLastByteOfAFrameArrives()
    {
        var bytes = FrameCodec.Encode(Sample);
        var decoder = new FrameDecoder();

        decoder.Append(bytes[..^1]);

        Assert.False(decoder.TryNext(out var frame));
        Assert.Null(frame);

        decoder.Append(bytes[^1..]);

        Assert.True(decoder.TryNext(out frame));
        Assert.NotNull(frame);
    }

    [Fact]
    public void EverySplitPointInAFrameDecodesToTheSameFrame()
    {
        // The interesting boundaries — inside the length prefix, inside the header length, in the
        // middle of the JSON, between header and body — are not special-cased, so sweep all of them.
        var expected = Frame.Picture(Guid.NewGuid(), WireRoom("Room 1"), "Haitao", "a.png", [1, 2, 3, 4, 5, 6, 7, 8], DateTime.Now);
        var bytes = FrameCodec.Encode(expected);

        for (var split = 1; split < bytes.Length; split++)
        {
            var decoder = new FrameDecoder();
            decoder.Append(bytes[..split]);
            Assert.False(decoder.TryNext(out _), $"frame decoded after only {split} of {bytes.Length} bytes");

            decoder.Append(bytes[split..]);
            Assert.True(decoder.TryNext(out var frame), $"frame lost when split at {split}");
            Assert.Equal(expected.Body, frame!.Body);
            Assert.Equal(expected.JsonHeader, frame.JsonHeader);
            Assert.Equal(0, decoder.BufferedBytes);
        }
    }

    [Fact]
    public void HalfAFrameIsKeptWhileTheRestIsStillOnTheWay()
    {
        var bytes = FrameCodec.Encode(Sample);
        var decoder = new FrameDecoder();

        decoder.Append(bytes[..2]);
        Assert.Empty(decoder.ReadAllAvailable());

        decoder.Append(bytes[2..6]);
        Assert.Empty(decoder.ReadAllAvailable());

        decoder.Append(bytes[6..]);
        Assert.Single(decoder.ReadAllAvailable());
    }

    [Fact]
    public void TheBufferShrinksBackToZeroAsFramesAreTakenOut()
    {
        var decoder = new FrameDecoder();
        for (var i = 0; i < 500; i++)
        {
            decoder.Append(FrameCodec.Encode(Frame.Text(Guid.NewGuid(), WireRoom("Room 1"), "Haitao", $"m{i}", DateTime.Now)));
        }

        var count = decoder.ReadAllAvailable().Count();

        Assert.Equal(500, count);
        Assert.Equal(0, decoder.BufferedBytes);
    }

    // ------------------------------------------------------------ corrupt input

    [Fact]
    public void AFrameBiggerThanTheLimitIsRefusedNotAllocated()
    {
        // The length prefix is supplied by the peer, so it must never size an allocation.
        var bytes = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)FrameDecoder.MaxFrameBytes + 1);

        var decoder = new FrameDecoder();
        decoder.Append(bytes);

        var error = Assert.Throws<FrameException>(() => decoder.TryNext(out _));
        Assert.Contains("exceeds", error.Message);
    }

    [Fact]
    public void AHeaderLengthRunningPastTheFrameIsRefused()
    {
        var bytes = FrameCodec.Encode(Sample);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(FrameDecoder.LengthPrefixBytes), (uint)bytes.Length * 4);

        var decoder = new FrameDecoder();
        decoder.Append(bytes);

        Assert.Throws<FrameException>(() => decoder.TryNext(out _));
    }

    [Fact]
    public void AFrameWhoseHeaderIsNotJsonIsRefused()
    {
        var json = "this is not json"u8.ToArray();
        var bytes = new byte[FrameDecoder.LengthPrefixBytes + FrameDecoder.HeaderLengthBytes + json.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)(FrameDecoder.HeaderLengthBytes + json.Length));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(FrameDecoder.LengthPrefixBytes), (uint)json.Length);
        json.CopyTo(bytes.AsSpan(FrameDecoder.LengthPrefixBytes + FrameDecoder.HeaderLengthBytes));

        var decoder = new FrameDecoder();
        decoder.Append(bytes);

        var error = Assert.Throws<FrameException>(() => decoder.TryNext(out _));
        Assert.Contains("not valid JSON", error.Message);
    }

    [Fact]
    public void AFrameTypeNeitherSideKnowsIsRefused()
    {
        // Adding a frame type in a newer build must not silently mis-parse on an older one.
        var json = """{"t":"TELEPORT"}"""u8.ToArray();
        var bytes = new byte[FrameDecoder.LengthPrefixBytes + FrameDecoder.HeaderLengthBytes + json.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)(FrameDecoder.HeaderLengthBytes + json.Length));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            bytes.AsSpan(FrameDecoder.LengthPrefixBytes), (uint)json.Length);
        json.CopyTo(bytes.AsSpan(FrameDecoder.LengthPrefixBytes + FrameDecoder.HeaderLengthBytes));

        var decoder = new FrameDecoder();
        decoder.Append(bytes);

        Assert.Throws<FrameException>(() => decoder.TryNext(out _));
    }

    // ----------------------------------------------------------- over a stream

    [Fact]
    public async Task AStreamThatReturnsOneBytePerReadIsHandled()
    {
        var bytes = FrameCodec.Encode(Sample).Concat(FrameCodec.Encode(Frame.Error("boom"))).ToArray();

        await using var stream = new DribbleStream(new MemoryStream(bytes));

        var first = await FrameCodec.ReadAsync(stream);
        var second = await FrameCodec.ReadAsync(stream);
        var third = await FrameCodec.ReadAsync(stream);

        Assert.Equal("hi", first!.Header.Text);
        Assert.Equal("boom", second!.Header.Error);
        Assert.Null(third);
    }

    [Fact]
    public async Task ReadingFramesFromAStreamKeepsItsPlace()
    {
        // ReadAsync consumes exactly one frame, so any leftover bytes stay in the stream for the
        // next call rather than being dropped on the floor.
        var expected = Enumerable.Range(0, 20).Select(i => Frame.Text(Guid.NewGuid(), WireRoom("Room 1"), "Haitao", $"m{i}", DateTime.Now)).ToArray();
        var bytes = expected.SelectMany(FrameCodec.Encode).ToArray();

        await using var stream = new MemoryStream(bytes);
        var read = new List<string?>();

        while (await FrameCodec.ReadAsync(stream) is { } frame)
        {
            read.Add(frame.Header.Text);
        }

        Assert.Equal(expected.Select(f => f.Header.Text), read);
    }

    [Fact]
    public async Task AConnectionClosedMidFrameIsAnErrorNotAMissingMessage()
    {
        var bytes = FrameCodec.Encode(Sample);
        await using var stream = new MemoryStream(bytes[..(bytes.Length / 2)]);

        var error = await Assert.ThrowsAsync<FrameException>(() => FrameCodec.ReadAsync(stream));
        Assert.Contains("missing", error.Message);
    }

    [Fact]
    public async Task AConnectionClosedBetweenFramesIsACleanEnd()
    {
        await using var stream = new MemoryStream(FrameCodec.Encode(Sample));

        Assert.NotNull(await FrameCodec.ReadAsync(stream));
        Assert.Null(await FrameCodec.ReadAsync(stream));
    }

    // ---------------------------------------------------------- domain mapping

    [Fact]
    public void ATextMessageBecomesATextFrameAndBack()
    {
        var message = new TextMessage("Haitao", "Room 1", "hi there", new DateTime(2026, 5, 6, 7, 8, 9));

        var back = DecodeSingle(FrameCodec.Encode(Frame.FromMessage(message, WireRoom(message.RoomName)))).ToMessage();

        Assert.Equal(FrameType.TEXT, Frame.FromMessage(message, WireRoom(message.RoomName)).Type);
        var text = Assert.IsType<TextMessage>(back);
        Assert.Equal(message.Id, text.Id);
        Assert.Equal(message.Body, text.Body);
        Assert.Equal(message.SenderNickname, text.SenderNickname);
        Assert.Equal(message.RoomName, text.RoomName);
        Assert.Equal(message.SentAt, text.SentAt);
    }

    [Fact]
    public void APictureKeepsItsBytesInTheBodyOnTheWayThrough()
    {
        var data = new byte[512];
        new Random(7).NextBytes(data);
        var message = new PictureMessage("Haitao", "Room 1", "shot.png", data, DateTime.Now);

        var frame = Frame.FromMessage(message, WireRoom(message.RoomName));
        var back = DecodeSingle(FrameCodec.Encode(frame)).ToMessage();

        Assert.Equal(FrameType.PICTURE, frame.Type);
        Assert.Equal(data, frame.Body);
        Assert.Equal(data, Assert.IsType<PictureMessage>(back).Data);
    }

    [Fact]
    public void AFileMessageBecomesAFileMetaFrame()
    {
        var message = new FileMessage("Haitao", "Room 1", "notes.txt", 4096, sentAt: DateTime.Now);

        var frame = Frame.FromMessage(message, WireRoom(message.RoomName));
        var back = DecodeSingle(FrameCodec.Encode(frame)).ToMessage();

        Assert.Equal(FrameType.FILE_META, frame.Type);
        Assert.Empty(frame.Body);
        Assert.Equal(message.TransferId, frame.Header.TransferId);
        var file = Assert.IsType<FileMessage>(back);
        Assert.Equal(message.TransferId, file.TransferId);
        Assert.Equal("notes.txt", file.FileName);
        Assert.Equal(4096, file.SizeBytes);
    }

    [Fact]
    public void ASysNoticeBecomesASystemNoticeFrame()
    {
        var frame = Frame.FromMessage(SystemMessage.Join("Room 1", "Victory"), WireRoom("Room 1"));

        var back = DecodeSingle(FrameCodec.Encode(frame)).ToMessage();

        Assert.Equal(FrameType.SYSTEM_NOTICE, frame.Type);
        Assert.Equal("Victory - join this chat", Assert.IsType<SystemMessage>(back).Body);
    }

    [Fact]
    public void FramesThatAreNotMessagesMapToNothing()
    {
        Frame[] frames = [Frame.Hello("Haitao"), Frame.RoomList([WireRoom("a")]), Frame.MemberList(WireRoomId, "Room 1", []), Frame.FileDone(Guid.NewGuid())];

        Assert.All(frames, frame => Assert.Null(frame.ToMessage()));
    }

    // ---------------------------------------------------------------- chunking

    [Fact]
    public async Task AFileIsCutIntoNumberedChunksWithNoBytesLostOrDuplicated()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lantern-chunk-{Guid.NewGuid():N}.bin");
        var payload = new byte[(FrameCodec.ChunkBytes * 2) + 4096];
        new Random(3).NextBytes(payload);

        try
        {
            await File.WriteAllBytesAsync(path, payload);
            var chunks = new List<(int Sequence, byte[] Bytes)>();

            await foreach (var chunk in FrameCodec.ChunkAsync(path))
            {
                chunks.Add(chunk);
            }

            Assert.Equal([0, 1, 2], chunks.Select(c => c.Sequence));
            Assert.Equal(FrameCodec.ChunkBytes, chunks[0].Bytes.Length);
            Assert.Equal(FrameCodec.ChunkBytes, chunks[1].Bytes.Length);
            Assert.Equal(4096, chunks[2].Bytes.Length);
            Assert.Equal(payload, chunks.SelectMany(c => c.Bytes).ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AnEmptyFileProducesNoChunks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lantern-empty-{Guid.NewGuid():N}.bin");

        try
        {
            await File.WriteAllBytesAsync(path, []);

            var count = 0;
            await foreach (var _ in FrameCodec.ChunkAsync(path))
            {
                count++;
            }

            Assert.Equal(0, count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ------------------------------------------------------------------ helpers

    private static Frame DecodeSingle(byte[] bytes)
    {
        var decoder = new FrameDecoder();
        decoder.Append(bytes);

        var frames = decoder.ReadAllAvailable().ToArray();

        Assert.Single(frames);
        Assert.Equal(0, decoder.BufferedBytes);
        return frames[0];
    }

    /// <summary>A stream that hands over at most one byte per read, the worst case a link can do.</summary>
    private sealed class DribbleStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel = default)
            => inner.ReadAsync(buffer[..1], cancel);

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, 1);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
