namespace Lantern.Domain;

/// <summary>
/// Announces a file that is being pushed to the room (F8). The bytes themselves travel
/// as FILE_CHUNK frames keyed by <see cref="TransferId"/>, not inside this message.
/// </summary>
public sealed class FileMessage : Message
{
    public FileMessage(string senderNickname, string roomName, string fileName, long sizeBytes,
        Guid? transferId = null, DateTime? sentAt = null, Guid? id = null)
        : base(senderNickname, roomName, MessageKind.FILE, sentAt, id)
    {
        FileName = fileName ?? string.Empty;
        SizeBytes = sizeBytes;
        TransferId = transferId ?? Guid.NewGuid();
    }

    public string FileName { get; }

    public long SizeBytes { get; }

    /// <summary>Ties this announcement to its FILE_META / FILE_CHUNK / FILE_DONE frames.</summary>
    public Guid TransferId { get; }

    public override string Preview => $"[file] {FileName} ({SizeBytes:N0} bytes)";
}
