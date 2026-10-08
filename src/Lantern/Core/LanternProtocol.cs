namespace Lantern.Core;

/// <summary>
/// Shared constants of the protocol and the app identity (T2.1, T4.3).
/// </summary>
public static class LanternProtocol
{
    /// <summary>
    /// The service class Lantern advertises over Bluetooth, so a client can tell a Lantern host from
    /// any other RFCOMM device. Fixed for the lifetime of the project: changing it would leave an
    /// older build unable to find a newer host.
    /// </summary>
    public static readonly Guid ServiceId = new("c1a5ee70-6c7a-4a54-9b7a-4c2b6f9d1a01");

    /// <summary>Human name shown in the Bluetooth service record and the client's server list.</summary>
    public const string ServiceName = "Lantern Chat";

    /// <summary>
    /// Bumped when the frame set changes incompatibly. HELLO carries no version yet: two builds with
    /// different frame sets fail as a corrupt-frame error rather than a clean "upgrade needed", which
    /// is acceptable while both machines always run the same build (see known limitations).
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// How far ahead of the slowest recipient the host may push file chunks, in chunks
    /// (<see cref="Transport.FrameCodec.ChunkBytes"/> each). Without a limit the whole file would be
    /// queued in memory for every person in the room - a 20 MB file to four people is 80 MB nobody asked
    /// for - and a sentence typed during the transfer would wait behind all of it. Eight chunks is 64 KB:
    /// a fraction of a second on a good link, and short enough that the chat still feels like a chat.
    /// </summary>
    public const int FileQueueChunks = 8;

    /// <summary>
    /// How long a file transfer may stay stuck behind a recipient that is not taking bytes before Lantern
    /// gives up on it. Long enough for a momentary stall, short enough that a machine that has stopped
    /// listening does not hold a transfer - and the memory of a queue - open forever.
    /// </summary>
    public static readonly TimeSpan FileFlowTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Largest file a machine will accept and write to disk. A peer could announce any size it likes in
    /// FILE_META, and without a ceiling one frame would be an invitation to fill somebody's disk. Generous
    /// on purpose: a lecture recording or a hundred photographs is not what this is guarding against.
    /// </summary>
    public const long MaxReceivedFileBytes = 512L * 1024 * 1024;

    /// <summary>How long a connection may take to introduce itself before it is dropped.</summary>
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Room list written to <c>rooms.json</c> when the file is missing or unreadable (F9).</summary>
    public static readonly string[] DefaultRoomNames = ["Room 1", "Room 2", "Room 3"];
}
