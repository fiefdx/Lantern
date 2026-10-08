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
/// <para>
/// The queue is unbounded, so the queue itself cannot be what keeps a file transfer inside a sensible
/// amount of memory. <see cref="QueuedBytes"/> is: it says how much is owed to this one person, and a
/// transfer that finds the number too big slows down at the source instead of quietly becoming a
/// warehouse. Slowing the sender is also the only honest response to a machine that cannot keep up -
/// dropping a chunk would corrupt the file, and dropping the connection would punish the room.
/// </para>
/// </summary>
public sealed class ChatServerSession : IParticipant
{
    private readonly ChatServer server;
    private readonly IClientLink link;
    private readonly Channel<Frame> outbox;
    private long framesDropped;
    private long queuedBytes;
    private int closed;

    public ChatServerSession(ChatServer server, IClientLink link)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.link = link ?? throw new ArgumentNullException(nameof(link));

        // Unbounded: a slow or speechless client must never block the thread that is broadcasting to
        // everybody else. What keeps that from being a licence to queue a whole file is the accounting
        // below, not the queue's own size.
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

    /// <summary>
    /// Bytes queued for this connection that have not been written yet. File transfers keep this under
    /// <see cref="LanternProtocol.FileQueueChunks"/> chunks per recipient (F8), which is what bounds the
    /// memory a file costs - and what stops a sentence typed during a transfer waiting behind all of it.
    /// </summary>
    public long QueuedBytes => Interlocked.Read(ref queuedBytes);

    /// <summary>
    /// False once this connection has started closing, which is when its queue stops draining for good.
    /// A transfer that counts a closed connection among its recipients would wait for bytes that can
    /// never be written.
    /// </summary>
    public bool IsOpen => Volatile.Read(ref closed) == 0;

    /// <inheritdoc />
    public void Receive(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        Interlocked.Add(ref queuedBytes, frame.Body.Length);

        if (!outbox.Writer.TryWrite(frame))
        {
            Interlocked.Add(ref queuedBytes, -frame.Body.Length);
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
        Receive(Frame.HelloAck(nickname, server.ServerName));
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

                // Awaiting the handler is what lets a file chunk apply back-pressure: when the room cannot
                // take any more bytes this connection waits, its socket buffer fills, and the machine
                // sending the file slows down. Only this connection waits - the rest of the room keeps
                // talking, which is the part T2.4 insisted on.
                await server.HandleAsync(frame, this).ConfigureAwait(false);
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

                // Off the tally only once it has actually gone. A watermark that counted a frame as
                // delivered while it still sat in the buffer would be the same lie in the other direction,
                // and a file transfer would run ahead of the link again - which is exactly what this
                // number exists to stop.
                Interlocked.Add(ref queuedBytes, -frame.Body.Length);
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
    public void TryComplete()
    {
        Volatile.Write(ref closed, 1);
        outbox.Writer.TryComplete();
    }

    /// <summary>Closes the underlying link; the read loop then returns.</summary>
    public void Close()
    {
        TryComplete();
        link.Close();
    }

    public override string ToString() => $"{Nickname} ({ConnectionId})";
}
