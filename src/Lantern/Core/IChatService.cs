using Lantern.Domain;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// Everything the UI is allowed to know about chatting. The window builds a
/// <see cref="ChatServer"/> (host) or a <see cref="ChatClient"/> (client) and only ever talks
/// through this interface — it never touches <see cref="Transport"/> (see design section,
/// “the UI never talks to Bluetooth directly”).
/// </summary>
public interface IChatService
{
    string Nickname { get; }

    /// <summary>True on the host process, false on a client process.</summary>
    bool IsHost { get; }

    /// <summary>
    /// Rooms to show in the left panel. A snapshot, not a live collection: on a connected client this
    /// state is rewritten by a background read loop, and handing out the live list would let the UI
    /// enumerate it mid-update. Listen to <see cref="RoomsChanged"/> and read again.
    /// </summary>
    IReadOnlyList<Room> Rooms { get; }

    /// <summary>
    /// Members of the room the local user is currently in (F12). Snapshot for the same reason.
    /// </summary>
    IReadOnlyList<User> Members { get; }

    /// <summary>Start the host side (bind + accept). Host only.</summary>
    Task StartAsync(CancellationToken cancel = default);

    /// <summary>Connect to a host. Client only.</summary>
    Task ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default);

    /// <summary>Create a room. Host only; a client call is rejected.</summary>
    Task CreateRoomAsync(string name, CancellationToken cancel = default);

    /// <summary>Rename a room. Host only.</summary>
    /// <summary>
    /// Renames the room with this identity. Rooms are asked for by identity, never by name: the name is the
    /// thing being changed, and two windows can be looking at two different names for one room while the host
    /// is partway through typing the third.
    /// </summary>
    Task RenameRoomAsync(Guid roomId, string newName, CancellationToken cancel = default);

    /// <summary>Delete a room. Host only. Anyone inside is kicked with a notice (F11).</summary>
    /// <summary>Deletes the room with this identity. See <see cref="RenameRoomAsync"/> for why that is how rooms are asked for.</summary>
    Task DeleteRoomAsync(Guid roomId, CancellationToken cancel = default);

    /// <summary>
    /// Leave the current room, if any, and join <paramref name="name"/> (F6).
    /// A null or blank name means “join nothing”, i.e. just leave.
    /// </summary>
    /// <summary>
    /// Leaves whatever room this side is in and joins the room with this identity; <see langword="null"/>
    /// means join nothing (F6).
    /// <para>
    /// An identity rather than a name, because a name is what the host may change at any moment: asking for
    /// "Study" is asking for whichever room answers to that on the host's machine right now, which is not
    /// necessarily the row a person clicked. Names are still what rooms are called - see
    /// <see cref="Lantern.Domain.Room.Name"/> - they are just no longer what they are identified by.
    /// </para>
    /// </summary>
    Task JoinRoomAsync(Guid? roomId, CancellationToken cancel = default);

    /// <summary>Broadcast a message to the room the local user is in.</summary>
    Task SendAsync(Message message, CancellationToken cancel = default);

    /// <summary>
    /// Starts a file going to the current room and hands back the job to watch (F8).
    /// <para>
    /// It does not wait for the file. Twenty megabytes over Bluetooth is minutes of work, and a window
    /// that could not be typed in while it sent an attachment would be a window nobody used. Watch
    /// <see cref="FileTransferJob.Fraction"/> for progress, await
    /// <see cref="FileTransferJob.Finished"/> when something needs to know the transfer is over, and call
    /// <see cref="FileTransferJob.CancelTransfer"/> to stop it - which stops the sender too, not just this
    /// end of it.
    /// </para>
    /// </summary>
    FileTransferJob SendFile(string path);

    /// <summary>Save an incoming transfer, announced by <see cref="IncomingFile"/> (F8).</summary>
    Task SaveReceivedFileAsync(FileTransferJob job, string path, CancellationToken cancel = default);

    /// <summary>Stop the host / disconnect the client.</summary>
    Task ShutdownAsync();

    event EventHandler<Message>? MessageReceived;

    event EventHandler? RoomsChanged;

    event EventHandler? MembersChanged;

    event EventHandler<FileTransferJob>? IncomingFile;

    /// <summary>Short human text for the status bar.</summary>
    event EventHandler<string>? StatusChanged;

    event EventHandler<Exception>? ConnectionLost;
}
