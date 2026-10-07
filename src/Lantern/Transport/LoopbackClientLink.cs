using System.Net;
using System.Net.Sockets;

namespace Lantern.Transport;

/// <summary>
/// An <see cref="IClientLink"/> over a localhost TCP socket (T2.3).
/// <para>
/// This exists so the whole chat engine — handshake, sessions, rooms, broadcasting — can be built
/// and tested on one machine before Bluetooth enters the picture in M4. Nothing above this class
/// knows the difference: <c>ChatServer</c> and <c>ChatClient</c> only ever see
/// <see cref="IClientLink"/>, and the frame codec cannot tell a TCP stream from an RFCOMM one.
/// </para>
/// When M4 lands, <c>BtRfcommServer</c> and <c>BtRfcommClient</c> take over these jobs and this file
/// stays behind as the test transport, which is exactly what the split is for.
/// </summary>
public sealed class LoopbackClientLink : IClientLink
{
    private readonly TcpClient client;
    private readonly NetworkStream stream;
    private readonly object gate = new();
    private bool closed;

    public LoopbackClientLink(TcpClient client, string? connectionId = null)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));

        // Nagle would hold a short chat line for up to 40 ms waiting for more data to send with it.
        client.NoDelay = true;
        stream = client.GetStream();
        ConnectionId = connectionId ?? Guid.NewGuid().ToString("N");
        RemoteName = Describe(client.Client.RemoteEndPoint);
    }

    /// <summary>
    /// Same object in both directions: a <see cref="NetworkStream"/> is full duplex, and the codec
    /// reads on one task while the session's write queue writes on another.
    /// </summary>
    public Stream Input => stream;

    /// <inheritdoc />
    public Stream Output => stream;

    /// <inheritdoc />
    public string RemoteName { get; }

    /// <inheritdoc />
    public string ConnectionId { get; }

    /// <inheritdoc />
    public event EventHandler<Exception?>? Closed;

    /// <inheritdoc />
    public void Close()
    {
        Exception? reason;

        lock (gate)
        {
            if (closed)
            {
                return;
            }

            closed = true;

            // A socket still containing unread bytes reports a reset on close; that is what the peer
            // hanging up looks like, not a fault of ours.
            reason = null;

            try
            {
                stream.Dispose();
            }
            catch (IOException)
            {
            }

            client.Close();
        }

        Closed?.Invoke(this, reason);
    }

    public void Dispose() => Close();

    /// <summary>The local port this link is bound to, for logs and tests.</summary>
    public int LocalPort => client.Client.LocalEndPoint is IPEndPoint local ? local.Port : 0;

    /// <summary>
    /// Loopback addresses have no useful host name, and a reverse lookup would stall the connect for
    /// seconds, so the address is shown as it stands.
    /// </summary>
    private static string Describe(EndPoint? remote) => remote is null ? "loopback" : remote.ToString()!;
}
