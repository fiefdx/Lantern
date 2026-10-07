namespace Lantern.Domain;

/// <summary>A plain chat line (F7).</summary>
public sealed class TextMessage : Message
{
    public TextMessage(string senderNickname, string roomName, string body, DateTime? sentAt = null)
        : base(senderNickname, roomName, MessageKind.TEXT, sentAt)
        => Body = body ?? string.Empty;

    public string Body { get; }

    public override string Preview => Body;
}
