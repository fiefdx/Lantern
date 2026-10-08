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
        => new(roomName, JoinBody(nickname));

    public static SystemMessage Leave(string roomName, string nickname)
        => new(roomName, LeaveBody(nickname));

    public static SystemMessage Kick(string roomName, string nickname)
        => new(roomName, KickBody(nickname));

    /// <summary>
    /// Said when somebody joins a room that already holds that name. Not a refusal - a nickname is chosen
    /// once, on the way in, and there is no way to change it from inside a room, so turning somebody away
    /// for it would leave them outside with nothing they could do. The room is told instead, because two
    /// lines from "Alex" mean two different people and only the room knows it.
    /// </summary>
    public static SystemMessage NameShared(string roomName, string nickname, int howMany)
        => new(roomName, NameSharedBody(nickname, howMany));

    // The wording is needed on its own as well: a SYSTEM_NOTICE frame carries the text, not an
    // object, and the server builds those frames straight from a member list.
    public static string JoinBody(string nickname) => $"{nickname} - {JoinSuffix}";

    public static string LeaveBody(string nickname) => $"{nickname} - {LeaveSuffix}";

    public static string KickBody(string nickname) => $"{nickname} - {KickSuffix}";

    public static string NameSharedBody(string nickname, int howMany)
        => $"{nickname} - there are {howMany} people in this room called {nickname}";
}
