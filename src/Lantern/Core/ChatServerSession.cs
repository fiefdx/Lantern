using System.Net.Sockets;
using System.Threading.Channels;
using Lantern.Domain;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// One connected client, seen from the host (T2.4). Two tasks per connection: a read loop that turns
/// frames into server actions, and a write loop that drains a queue. They never share state, because
/// a connection must keep receiving while it is still trying to send — and a client that has stopped
/// reading must not be able to stop the server.
/// </summary>
public sealed class ChatServerSession : IParticipant
{
    private readonly ChatServer server;
    private readonly IClientLink link;
    private readonly Channel<Frame> outbox;
    private long framesDropped;

    public ChatServerSession(ChatServer server, IClientLink link)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.link = link ?? throw new ArgumentNullException(nameof(link));

        // Unbounded: a slow or speechless client must never block the thread that is broadcasting to
        // everybody else. The cost is memory, and M5 bounds this queue when file chunks arrive.
        outbox = Channel.CreateUnbounded<Frame>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public string ConnectionId => link.ConnectionId;

    /// <inheritdoc />
    public string Nickname { get; private set; } = string.Empty;

    /// <summary>Room this client is in, or null between rooms. Only the server writes it.</summary>
    public Room? CurrentRoom { get; set; }

    /// <summary>Why the connection ended, for the status line and the tests.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>Frames refused because the connection was already going away.</summary>
    public long FramesDropped => Interlocked.Read(ref framesDropped);

    /// <inheritdoc />
    public void Receive(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!outbox.Writer.TryWrite(frame))
        {
            Interlocked.Increment(ref framesDropped);
        }
    }

    /// <summary>
    /// Reads HELLO, takes the nickname from it and answers HELLO_ACK.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the connection never introduced itself — which is normal for a
    /// discovery probe (see <see cref="BtLoopbackClient.DiscoverAsync"/>) and is not worth a notice.
    /// </returns>
    public async Task<bool> HandshakeAsync(CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(LanternProtocol.HandshakeTimeout);

        Frame? hello;
        try
        {
            hello = await FrameCodec.ReadAsync(link.Input, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FrameException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }

        if (hello is null || hello.Type != FrameType.HELLO)
        {
            return false;
        }

        var nickname = ValidationRules.Normalize(hello.Header.Nickname);
        if (ValidationRules.NicknameError(nickname) is { } error)
        {
            // Best effort: the peer may already be gone.
            Receive(Frame.Error(error));
            TryComplete();
            return false;
        }

        Nickname = nickname;
        Receive(Frame.HelloAck(nickname));
        return true;
    }

    /// <summary>
    /// Reads frames until the link ends. Returns the reason it ended, or null for a clean close —
    /// the caller turns that into "client left" and updates the rooms.
    /// </summary>
    public async Task<Exception?> ReadLoopAsync(CancellationToken cancel)
    {
        try
        {
            while (true)
            {
                var frame = await FrameCodec.ReadAsync(link.Input, cancel).ConfigureAwait(false);
                if (frame is null)
                {
                    return null;
                }

                server.Handle(frame, this);
            }
        }
        catch (Exception ex) when (ex is FrameException or IOException or SocketException or ObjectDisposedException)
        {
            return ex;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Writes queued frames in order until the queue is completed or the link fails.</summary>
    public async Task WriteLoopAsync(CancellationToken cancel)
    {
        try
        {
            await foreach (var frame in outbox.Reader.ReadAllAsync(cancel).ConfigureAwait(false))
            {
                await FrameCodec.WriteAsync(link.Output, frame, cancel).ConfigureAwait(false);
                await link.Output.FlushAsync(cancel).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is FrameException or IOException or SocketException or ObjectDisposedException)
        {
            Failure = ex;
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>No more frames will be queued; lets <see cref="WriteLoopAsync"/> finish.</summary>
    public void TryComplete() => outbox.Writer.TryComplete();

    /// <summary>Closes the underlying link; the read loop then returns.</summary>
    public void Close()
    {
        TryComplete();
        link.Close();
    }

    public override string ToString() => $"{Nickname} ({ConnectionId})";
}
