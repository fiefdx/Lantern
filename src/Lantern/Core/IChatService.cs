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
    Task RenameRoomAsync(string currentName, string newName, CancellationToken cancel = default);

    /// <summary>Delete a room. Host only. Anyone inside is kicked with a notice (F11).</summary>
    Task DeleteRoomAsync(string name, CancellationToken cancel = default);

    /// <summary>
    /// Leave the current room, if any, and join <paramref name="name"/> (F6).
    /// A null or blank name means “join nothing”, i.e. just leave.
    /// </summary>
    Task JoinRoomAsync(string? name, CancellationToken cancel = default);

    /// <summary>Broadcast a message to the room the local user is in.</summary>
    Task SendAsync(Message message, CancellationToken cancel = default);

    /// <summary>Send a whole file to the room (F8).</summary>
    Task SendFileAsync(string path, CancellationToken cancel = default);

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
