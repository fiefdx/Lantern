using Lantern.Domain;

namespace Lantern.Transport;

/// <summary>
/// One unit of conversation between two Lantern processes: a JSON header plus an optional binary
/// body (a picture, or a slice of a file). RFCOMM is a byte stream with no message boundaries, so
/// <see cref="FrameCodec"/> length-prefixes each frame before it goes out — see that class.
/// </summary>
public sealed class Frame
{
    public Frame(FrameHeader header, byte[]? body = null)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Body = body ?? [];
    }

    /// <summary>What kind of frame this is, taken from the header.</summary>
    public FrameType Type => Header.Type;

    /// <summary>The typed view of the header.</summary>
    public FrameHeader Header { get; }

    /// <summary>Binary payload; empty for every frame type except PICTURE and FILE_CHUNK.</summary>
    public byte[] Body { get; }

    /// <summary>The exact JSON that goes on the wire — handy in tests and packet traces.</summary>
    public string JsonHeader => Header.ToJson();

    // ------------------------------------------------------------- factories

    public static Frame Hello(string nickname) => new(new FrameHeader
    {
        Type = FrameType.HELLO,
        Nickname = nickname,
    });

    public static Frame HelloAck(string nickname, string? serverName = null) => new(new FrameHeader
    {
        Type = FrameType.HELLO_ACK,
        Nickname = nickname,
        ServerName = serverName,
    });

    /// <summary>
    /// The room list, with each room's identity beside its name. This frame is where a rename arrives: the
    /// same identity answering to a different name, rather than one room going away and another appearing.
    /// </summary>
    public static Frame RoomList(IEnumerable<Room> rooms) => new(new FrameHeader
    {
        Type = FrameType.ROOM_LIST,
        Rooms = rooms.Select(r => new FrameRoom(r.Id, r.Name)).ToArray(),
    });

    /// <summary>Ask to be put in a room. Nothing to join - the empty case - is how "leave" travels.</summary>
    public static Frame JoinRoom(Guid? roomId) => new(new FrameHeader
    {
        Type = FrameType.JOIN_ROOM,
        RoomId = roomId,
    });

    /// <summary>
    /// A chat line, with the formatting it was sent with when there was any (T12.1). The sentence goes in
    /// <c>text</c> exactly as typed and the runs go beside it; nothing here merges the two.
    /// </summary>
    public static Frame Text(Guid id, Room room, string nickname, string text, DateTime sentAt,
        IReadOnlyList<StyledRun>? styles = null) => new(new FrameHeader
    {
        Type = FrameType.TEXT,
        Id = id,
        RoomId = room.Id,
        Room = room.Name,
        Nickname = nickname,
        Text = text,
        SentAt = sentAt,
        Styles = RunsForWire(styles),
    });

    /// <summary>
    /// The runs as they go out, or null for none. A message nobody formatted sends no style field at all —
    /// which is what a v1.6 peer sent, and what keeps an old frame and a new one comparable — and an empty
    /// array is never sent either: a list of no runs describes no sentence, so it would be refused on the way
    /// in. The honest way to say "plain" is to say nothing.
    /// </summary>
    private static List<StyleDto>? RunsForWire(IReadOnlyList<StyledRun>? runs)
        => runs is null or { Count: 0 } ? null : runs.Select(RunForWire).ToList();

    private static StyleDto RunForWire(StyledRun run) => new()
    {
        Length = run.Length,
        Bold = run.Bold,
        Italic = run.Italic,
        Underline = run.Underline,
        Color = run.Color,
        // "Normal" is the size this window would have used anyway, so asking for it is silence. A size that
        // is not one of the three is refused where it arrives rather than rewritten here: a sender that
        // invents sizes needs to find out, and a receiver must never be handed one to parse.
        Size = run.Size == StyledRun.Normal ? null : run.Size,
    };

    /// <summary>
    /// The formatting this frame's sentence arrives with, already judged: no field, an empty list, or a list
    /// that does not add up to the sentence all arrive as no formatting at all, and the words are unaffected
    /// whichever it was. Runs whose size or colour nobody could render go the same way.
    /// </summary>
    /// <remarks>
    /// This is where a peer's formatting is accepted, so this is where it is believed or refused. Both the
    /// transcript line (<see cref="ToMessage"/>) and the host relaying a frame read their runs through here,
    /// so a message cannot be plain in one window and formatted in another.
    /// </remarks>
    public IReadOnlyList<StyledRun> TextStyles()
    {
        if (Header.Styles is null or { Count: 0 })
        {
            return Array.Empty<StyledRun>();
        }

        return StyledRun.Accepted(Header.Styles.Select(RunFromWire).ToList(), Header.Text);
    }

    private static StyledRun RunFromWire(StyleDto style)
        => new(style.Length, style.Bold, style.Italic, style.Underline, style.Size ?? StyledRun.Normal, style.Color);

    public static Frame Picture(Guid id, Room room, string nickname, string fileName, byte[] data, DateTime sentAt) => new(
        new FrameHeader
        {
            Type = FrameType.PICTURE,
            Id = id,
            RoomId = room.Id,
            Room = room.Name,
            Nickname = nickname,
            FileName = fileName,
            SizeBytes = data.LongLength,
            SentAt = sentAt,
        },
        data);

    /// <param name="roomId">Which room the file belongs to, by identity.</param>
    /// <param name="roomName">What that room is called, for the transcript line every window prints.</param>
    public static Frame FileMeta(Guid messageId, Guid transferId, Guid roomId, string roomName, string nickname, string fileName, long sizeBytes) => new(new FrameHeader
    {
        Type = FrameType.FILE_META,
        Id = messageId,
        TransferId = transferId,
        RoomId = roomId,
        Room = roomName,
        Nickname = nickname,
        FileName = fileName,
        SizeBytes = sizeBytes,
    });

    public static Frame FileChunk(Guid transferId, int sequence, byte[] bytes) => new(
        new FrameHeader
        {
            Type = FrameType.FILE_CHUNK,
            TransferId = transferId,
            Sequence = sequence,
            SizeBytes = bytes.LongLength,
        },
        bytes);

    public static Frame FileDone(Guid transferId) => new(new FrameHeader
    {
        Type = FrameType.FILE_DONE,
        TransferId = transferId,
    });

    public static Frame FileCancel(Guid transferId) => new(new FrameHeader
    {
        Type = FrameType.FILE_CANCEL,
        TransferId = transferId,
    });

    /// <summary>
    /// Who is in a room. The room is named by identity, including the empty case
    /// (<c>roomId: null</c>) that says "this side is standing nowhere" - a bare empty list cannot tell
    /// "nobody is here" from "the room you were in was closed".
    /// </summary>
    public static Frame MemberList(Guid? roomId, string? roomName, IEnumerable<User> members) => new(new FrameHeader
    {
        Type = FrameType.MEMBER_LIST,
        RoomId = roomId,
        Room = roomName,
        Members = members.Select(m => new FrameMember(m.ConnectionId, m.Nickname)).ToArray(),
    });

    public static Frame SystemNotice(Room room, string notice) => new(new FrameHeader
    {
        Type = FrameType.SYSTEM_NOTICE,
        RoomId = room.Id,
        Room = room.Name,
        Text = notice,
        SentAt = DateTime.Now,
    });

    public static Frame Error(string message, Room? room = null) => new(new FrameHeader
    {
        Type = FrameType.ERROR,
        RoomId = room?.Id,
        Room = room?.Name,
        Error = message,
    });

    // -------------------------------------------------------- domain mapping

    /// <summary>
    /// Wraps a domain message in the frame that carries it. A FILE_MESSAGE becomes FILE_META: the
    /// bytes follow as numbered FILE_CHUNK frames, never inside this one.
    /// <para>
    /// The room is said here rather than taken from the message, because the room is not the message's to
    /// say: the side broadcasting decides which room a line belongs to, and its name at that moment is the
    /// name that goes out. A message quoting an older name for the room is history, not an instruction.
    /// </para>
    /// </summary>
    public static Frame FromMessage(Message message, Room room) => message switch
    {
        // The runs travel with the line they annotate, or are left off when there were none. Never folded
        // into the body: a message whose formatting was refused must still say what it said.
        TextMessage text => Text(text.Id, room, text.SenderNickname, text.Body, text.SentAt, text.Styles),
        PictureMessage picture => Picture(picture.Id, room, picture.SenderNickname, picture.FileName, picture.Data, picture.SentAt),
        FileMessage file => FileMeta(file.Id, file.TransferId, room.Id, room.Name, file.SenderNickname, file.FileName, file.SizeBytes),
        SystemMessage notice => SystemNotice(room, notice.Body),
        _ => throw new ArgumentException($"Unknown message type {message.GetType().Name}.", nameof(message)),
    };

    /// <summary>
    /// Rebuilds the transcript entry a frame announces. Frame types that are not messages — the
    /// handshake, room and member lists, chunks — return null and are handled by the session.
    /// </summary>
    public Message? ToMessage()
    {
        var room = Header.Room ?? string.Empty;
        var nick = Header.Nickname ?? string.Empty;
        var at = Header.SentAt ?? DateTime.Now;

        // The sender's message id is the identity of the message, so it is passed straight back
        // into the constructor: the line a receiver shows is the line the sender sent, and a
        // duplicated frame can be spotted by id.
        return Type switch
        {
            // The sentence, plus whatever formatting survived the trip with it. The words were already read
            // out of the header above, so a frame whose styles are nonsense still lands here as a message.
            FrameType.TEXT => new TextMessage(nick, room, Header.Text ?? string.Empty, at, Header.Id) { Styles = TextStyles() },
            FrameType.PICTURE => new PictureMessage(nick, room, Header.FileName ?? "image", Body, at, Header.Id),
            FrameType.FILE_META => new FileMessage(nick, room, Header.FileName ?? "file", Header.SizeBytes ?? 0, Header.TransferId, at, Header.Id),
            FrameType.SYSTEM_NOTICE => new SystemMessage(room, Header.Text ?? string.Empty, at, Header.Id),
            _ => null,
        };
    }
}

/// <summary>A frame that could not be read: bad length, bad JSON or an unknown type.</summary>
public sealed class FrameException(string message, Exception? inner = null) : Exception(message, inner);
