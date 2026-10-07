namespace Lantern.Domain;

/// <summary>
/// A server-generated notice, e.g. “Cara - join this chat” (F11).
/// </summary>
public sealed class SystemMessage : Message
{
    public const string JoinSuffix = "join this chat";
    public const string LeaveSuffix = "left this chat";
    public const string KickSuffix = "was removed from this chat";

    public SystemMessage(string roomName, string body, DateTime? sentAt = null, Guid? id = null)
        : base(string.Empty, roomName, MessageKind.JOIN_LEAVE_NOTICE, sentAt, id)
        => Body = body ?? string.Empty;

    public string Body { get; }

    public override string Preview => Body;

    /// <summary>“Cara - join this chat”, matching the wireframe wording.</summary>
    public static SystemMessage Join(string roomName, string nickname)
        => new(roomName, $"{nickname} - {JoinSuffix}");

    public static SystemMessage Leave(string roomName, string nickname)
        => new(roomName, $"{nickname} - {LeaveSuffix}");

    public static SystemMessage Kick(string roomName, string nickname)
        => new(roomName, $"{nickname} - {KickSuffix}");
}
