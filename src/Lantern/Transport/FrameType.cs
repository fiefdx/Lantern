namespace Lantern.Transport;

/// <summary>
/// The twelve things that can travel between two Lantern processes (T2.1). The member names are
/// the wire names: <see cref="FrameCodec"/> serialises the enum as its own name, so a packet trace
/// reads like this list and adding a member cannot silently renumber an existing one.
/// </summary>
public enum FrameType
{
    /// <summary>Client → server, first frame on a connection: carries the nickname.</summary>
    HELLO,

    /// <summary>Server → client: the handshake worked, the session is live.</summary>
    HELLO_ACK,

    /// <summary>Server → client: the full room list (F5, F6, F9). Sent after every room CRUD.</summary>
    ROOM_LIST,

    /// <summary>Client → server: join this room (and implicitly leave the previous one, F6).</summary>
    JOIN_ROOM,

    /// <summary>A chat line (F7).</summary>
    TEXT,

    /// <summary>An inline picture: JSON header plus the image bytes as the frame body (F7).</summary>
    PICTURE,

    /// <summary>A file is about to be pushed: name, size and transfer id (F8).</summary>
    FILE_META,

    /// <summary>One slice of a file's bytes, numbered so the receiver can detect a gap (F8).</summary>
    FILE_CHUNK,

    /// <summary>The last chunk has been sent (F8).</summary>
    FILE_DONE,

    /// <summary>Server → client: who is in the room right now (F12).</summary>
    MEMBER_LIST,

    /// <summary>Server → client: "X - join this chat" and friends (F11).</summary>
    SYSTEM_NOTICE,

    /// <summary>Something the other side asked for could not be done; carries the reason.</summary>
    ERROR,
}
