namespace Lantern.Domain;

/// <summary>
/// What kind of transcript entry this is. Also drives the frame type used on the wire.
/// </summary>
public enum MessageKind
{
    /// <summary>Plain chat text.</summary>
    TEXT,

    /// <summary>An inline picture (F7).</summary>
    PICTURE,

    /// <summary>A file offered to the room (F8).</summary>
    FILE,

    /// <summary>A "X - join this chat" style line (F11).</summary>
    JOIN_LEAVE_NOTICE,
}
