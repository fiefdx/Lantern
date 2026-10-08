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

    public static Frame RoomList(IEnumerable<string> roomNames) => new(new FrameHeader
    {
        Type = FrameType.ROOM_LIST,
        Rooms = roomNames.ToArray(),
    });

    public static Frame JoinRoom(string roomName) => new(new FrameHeader
    {
        Type = FrameType.JOIN_ROOM,
        Room = roomName,
    });

    public static Frame Text(Guid id, string roomName, string nickname, string text, DateTime sentAt) => new(new FrameHeader
    {
        Type = FrameType.TEXT,
        Id = id,
        Room = roomName,
        Nickname = nickname,
        Text = text,
        SentAt = sentAt,
    });

    public static Frame Picture(Guid id, string roomName, string nickname, string fileName, byte[] data, DateTime sentAt) => new(
        new FrameHeader
        {
            Type = FrameType.PICTURE,
            Id = id,
            Room = roomName,
            Nickname = nickname,
            FileName = fileName,
            SizeBytes = data.LongLength,
            SentAt = sentAt,
        },
        data);

    public static Frame FileMeta(Guid messageId, Guid transferId, string roomName, string nickname, string fileName, long sizeBytes) => new(new FrameHeader
    {
        Type = FrameType.FILE_META,
        Id = messageId,
        TransferId = transferId,
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

    public static Frame MemberList(string roomName, IEnumerable<User> members) => new(new FrameHeader
    {
        Type = FrameType.MEMBER_LIST,
        Room = roomName,
        Members = members.Select(m => new FrameMember(m.ConnectionId, m.Nickname)).ToArray(),
    });

    public static Frame SystemNotice(string roomName, string notice) => new(new FrameHeader
    {
        Type = FrameType.SYSTEM_NOTICE,
        Room = roomName,
        Text = notice,
        SentAt = DateTime.Now,
    });

    public static Frame Error(string message, string? roomName = null) => new(new FrameHeader
    {
        Type = FrameType.ERROR,
        Room = roomName,
        Error = message,
    });

    // -------------------------------------------------------- domain mapping

    /// <summary>
    /// Wraps a domain message in the frame that carries it. A FILE_MESSAGE becomes FILE_META: the
    /// bytes follow as numbered FILE_CHUNK frames, never inside this one.
    /// </summary>
    public static Frame FromMessage(Message message) => message switch
    {
        TextMessage text => Text(text.Id, text.RoomName, text.SenderNickname, text.Body, text.SentAt),
        PictureMessage picture => Picture(picture.Id, picture.RoomName, picture.SenderNickname, picture.FileName, picture.Data, picture.SentAt),
        FileMessage file => FileMeta(file.Id, file.TransferId, file.RoomName, file.SenderNickname, file.FileName, file.SizeBytes),
        SystemMessage notice => SystemNotice(notice.RoomName, notice.Body),
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
            FrameType.TEXT => new TextMessage(nick, room, Header.Text ?? string.Empty, at, Header.Id),
            FrameType.PICTURE => new PictureMessage(nick, room, Header.FileName ?? "image", Body, at, Header.Id),
            FrameType.FILE_META => new FileMessage(nick, room, Header.FileName ?? "file", Header.SizeBytes ?? 0, Header.TransferId, at, Header.Id),
            FrameType.SYSTEM_NOTICE => new SystemMessage(room, Header.Text ?? string.Empty, at, Header.Id),
            _ => null,
        };
    }
}

/// <summary>A frame that could not be read: bad length, bad JSON or an unknown type.</summary>
public sealed class FrameException(string message, Exception? inner = null) : Exception(message, inner);
