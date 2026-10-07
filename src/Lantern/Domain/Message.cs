namespace Lantern.Domain;

/// <summary>
/// Base class for everything that shows up in a room's transcript (Figure 7).
/// Immutable value semantics: a message is created once and then only moved around.
/// </summary>
public abstract class Message
{
    /// <param name="sentAt">Defaults to now.</param>
    /// <param name="id">
    /// Defaults to a new Guid. The wire layer passes the id that arrived in the frame, so the
    /// message a receiver shows is the same message the sender sent — which is what makes a
    /// duplicate frame recognisable.
    /// </param>
    protected Message(string senderNickname, string roomName, MessageKind kind, DateTime? sentAt = null, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        SenderNickname = senderNickname ?? string.Empty;
        RoomName = roomName ?? string.Empty;
        Kind = kind;
        SentAt = sentAt ?? DateTime.Now;
    }

    /// <summary>Globally unique; also used to de-duplicate re-delivered frames.</summary>
    public Guid Id { get; }

    /// <summary>Who typed it. Empty for server-generated notices.</summary>
    public string SenderNickname { get; }

    /// <summary>When it left the sender.</summary>
    public DateTime SentAt { get; }

    /// <summary>The room it belongs to; carried along so one frame is self-describing.</summary>
    public string RoomName { get; }

    /// <summary>Discriminator, so the wire layer can rebuild the right subclass.</summary>
    public MessageKind Kind { get; }

    /// <summary>Single-line text used for the transcript and for tests.</summary>
    public abstract string Preview { get; }

    public override string ToString() => $"{SentAt:HH:mm:ss} {SenderNickname}: {Preview}";
}
