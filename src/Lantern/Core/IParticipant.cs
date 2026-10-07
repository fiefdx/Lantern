using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// Anything that can be a member of a room on the server: a connected client
/// (<see cref="ChatServerSession"/>) or the host's own window (<see cref="LocalParticipant"/>).
/// <para>
/// The server does not care how a member is reached, only that it has an id, a nickname and a way to
/// be handed a frame. That is what lets the person hosting the chat sit in a room with everyone else
/// without special cases in the join, broadcast and member-list code.
/// </para>
/// </summary>
public interface IParticipant
{
    /// <summary>Stable for the connection, unique among connected members. Keys the session table.</summary>
    string ConnectionId { get; }

    /// <summary>Nickname announced in HELLO; two people may share one, ids may not.</summary>
    string Nickname { get; }

    /// <summary>
    /// Hands a frame to this member. Must not block: it is called while the server holds its room
    /// lock, so implementations queue the frame and let their own writer deliver it.
    /// </summary>
    void Receive(Frame frame);
}

/// <summary>
/// The host's own participation, in process. Frames arrive as an event instead of travelling over a
/// socket, which keeps the host out of the loopback path while still being an ordinary room member.
/// </summary>
public sealed class LocalParticipant : IParticipant
{
    /// <summary>The id reserved for the host's own window.</summary>
    public const string Id = "host-local";

    /// <inheritdoc />
    public string ConnectionId => Id;

    /// <inheritdoc />
    public string Nickname { get; }

    public LocalParticipant(string nickname) => Nickname = nickname;

    /// <summary>Every frame the server sends to this member, in order.</summary>
    public event EventHandler<Frame>? FrameReceived;

    /// <inheritdoc />
    /// <remarks>
    /// The host's window is reached this way, from inside the server's own code, so delivery is
    /// wrapped: a window that throws while receiving a frame must not abort the server's join or
    /// broadcast halfway through.
    /// </remarks>
    public void Receive(Frame frame) => Dispatch.Safe(() => FrameReceived?.Invoke(this, frame));
}
