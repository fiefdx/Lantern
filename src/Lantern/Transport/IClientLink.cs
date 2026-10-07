namespace Lantern.Transport;

/// <summary>
/// One connected pair of endpoints. The wire protocol (see <c>FrameCodec</c>) is spoken over
/// <see cref="Input"/>/<see cref="Output"/>; nothing in this project knows whether the
/// streams are RFCOMM, localhost TCP or in-memory.
/// </summary>
public interface IClientLink : IDisposable
{
    /// <summary>Read side of the connection.</summary>
    Stream Input { get; }

    /// <summary>Write side of the connection.</summary>
    Stream Output { get; }

    /// <summary>Identifier of the remote end (computer name, or a generated one for loopback).</summary>
    string RemoteName { get; }

    /// <summary>Opaque id used as the session key on the server side.</summary>
    string ConnectionId { get; }

    /// <summary>Raised once when the link drops; the argument is the reason, or null if clean.</summary>
    event EventHandler<Exception?> Closed;

    /// <summary>Idempotent.</summary>
    void Close();
}
