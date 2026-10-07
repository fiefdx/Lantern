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

    /// <summary>How long a connection may take to introduce itself before it is dropped.</summary>
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Room list written to <c>rooms.json</c> when the file is missing or unreadable (F9).</summary>
    public static readonly string[] DefaultRoomNames = ["Room 1", "Room 2", "Room 3"];
}
