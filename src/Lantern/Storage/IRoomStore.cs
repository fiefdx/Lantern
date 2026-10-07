using Lantern.Domain;

namespace Lantern.Storage;

/// <summary>
/// Room-name persistence. Only names are stored — never members, never history (F9).
/// The host owns the store; a client never sees it.
/// </summary>
public interface IRoomStore
{
    /// <summary>
    /// Read the room list. A missing or corrupt file must return the default rooms and must
    /// never throw.
    /// </summary>
    List<Room> Load();

    /// <summary>Persist the room list. Off the UI thread; safe to await.</summary>
    Task SaveAsync(IEnumerable<Room> rooms, CancellationToken cancel = default);

    /// <summary>
    /// Raised when a save could not be completed, with a reason fit for the host's status line.
    /// A store that cannot write reports this instead of throwing: the rooms in memory are still right,
    /// the chat is still running, and interrupting a conversation over a file is the wrong trade.
    /// </summary>
    event EventHandler<string>? SaveFailed;
}
