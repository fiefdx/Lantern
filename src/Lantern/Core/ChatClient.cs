using System.Net.Sockets;
using Lantern.Domain;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// The client side of a chat (T2.5): connects, introduces itself, then spends its life in a read loop
/// turning frames into state and events. It is an <see cref="IChatService"/>, so a window cannot tell
/// it apart from a host except through <see cref="IsHost"/>.
/// <para>
/// The read loop is the only writer of the state, and it runs on a thread-pool thread. That is why
/// <see cref="Rooms"/> and <see cref="Members"/> are snapshots and why every event is raised outside
/// the lock: a window must never enumerate a list that a MEMBER_LIST frame is half way through
/// replacing.
/// </para>
/// </summary>
public sealed class ChatClient : IChatService, IAsyncDisposable
{
    private readonly IBtClient transport;
    private readonly ClientState state = new();
    private readonly SemaphoreSlim writeLock = new(1, 1);

    private IClientLink? link;
    private Task? readLoop;
    private TaskCompletionSource<Frame>? helloAck;
    private bool shuttingDown;

    public ChatClient(string nickname, IBtClient? transport = null)
    {
        Nickname = ValidationRules.Normalize(nickname);

        if (ValidationRules.NicknameError(Nickname) is { } error)
        {
            throw new ArgumentException(error, nameof(nickname));
        }

        this.transport = transport ?? new BtLoopbackClient();

        state.RoomsChanged += (_, _) => RoomsChanged?.Invoke(this, EventArgs.Empty);
        state.MembersChanged += (_, _) => MembersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public string Nickname { get; }

    /// <inheritdoc />
    public bool IsHost => false;

    /// <inheritdoc />
    public IReadOnlyList<Room> Rooms => state.Rooms;

    /// <inheritdoc />
    public IReadOnlyList<User> Members => state.Members;

    /// <summary>The server this client is, or was, talking to.</summary>
    public ServerEndpoint? Endpoint { get; private set; }

    /// <summary>Room this client is in, or null. Mirrors what the server believes.</summary>
    public string? CurrentRoomName => state.CurrentRoomName;

    public bool IsConnected => link is not null;

    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler<Message>? MessageReceived;

    /// <inheritdoc />
    public event EventHandler? RoomsChanged;

    /// <inheritdoc />
    public event EventHandler? MembersChanged;

    // Nothing on a client raises these until file transfer lands in M5 (T5.2, T5.3); they are part of
    // the interface the UI binds to, so they exist now and stay quiet until then.
#pragma warning disable CS0067
    /// <inheritdoc />
    public event EventHandler<FileTransferJob>? IncomingFile;

    /// <inheritdoc />
    public event EventHandler<string>? FileSaved;
#pragma warning restore CS0067

    /// <summary>
    /// The connection ended on its own (scenario 6). The exception's message is the reason, worded for
    /// the status bar rather than for a log.
    /// </summary>
    public event EventHandler<Exception>? ConnectionLost;

    /// <summary>The host refused something (an ERROR frame). Shown in the status bar.</summary>
    public event EventHandler<string>? ServerError;

    // ------------------------------------------------------------- connection

    /// <summary>Connects, says HELLO, waits for HELLO_ACK and starts the read loop (T2.5).</summary>
    public async Task ConnectAsync(ServerEndpoint endpoint, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (link is not null)
        {
            throw new InvalidOperationException("Already connected.");
        }

        Endpoint = endpoint;
        Status($"connecting to {endpoint}…");

        var newLink = await transport.ConnectAsync(endpoint, cancel).ConfigureAwait(false);
        link = newLink;

        var ack = new TaskCompletionSource<Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        helloAck = ack;

        // The read loop has to be running before the HELLO goes out: the answer arrives on it.
        readLoop = Task.Run(() => ReadLoopAsync(newLink), CancellationToken.None);

        await WriteAsync(Frame.Hello(Nickname), cancel).ConfigureAwait(false);

        var completed = await Task.WhenAny(ack.Task, Task.Delay(LanternProtocol.HandshakeTimeout, cancel)).ConfigureAwait(false);

        if (completed != ack.Task)
        {
            await DisconnectAsync("The server did not answer the Lantern handshake.").ConfigureAwait(false);
            throw new InvalidOperationException("The server did not answer the Lantern handshake. Is it a Lantern host?");
        }

        var acknowledged = await ack.Task.ConfigureAwait(false);

        // Discovery can only report an address; HELLO_ACK is where the host says what to call it.
        if (acknowledged.Header.ServerName is { Length: > 0 } hostName)
        {
            Endpoint = Endpoint with { ServerName = hostName };
        }

        Status($"connected to {Endpoint}");
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancel = default)
        => throw new InvalidOperationException("A client does not listen for connections.");

    /// <inheritdoc />
    public async Task JoinRoomAsync(string? name, CancellationToken cancel = default)
    {
        var wanted = ValidationRules.Normalize(name);
        state.ExpectRoom(wanted);

        // An empty room name is how "leave the current room" travels on the wire.
        await WriteOrThrowAsync(Frame.JoinRoom(wanted), cancel).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SendAsync(Message message, CancellationToken cancel = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message is not TextMessage text)
        {
            throw new NotSupportedException("This build sends text messages only.");
        }

        if (ValidationRules.MessageError(text.Body) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        await WriteOrThrowAsync(Frame.FromMessage(text), cancel).ConfigureAwait(false);

        // The host does not echo a message to its author, so this side keeps its own copy or the
        // transcript would lose every line the user typed when they next enter the room.
        state.Record(text);
    }

    // -------------------------------------------------------- host-only actions

    /// <inheritdoc />
    public Task CreateRoomAsync(string name, CancellationToken cancel = default) => HostOnly();

    /// <inheritdoc />
    public Task RenameRoomAsync(string currentName, string newName, CancellationToken cancel = default) => HostOnly();

    /// <inheritdoc />
    public Task DeleteRoomAsync(string name, CancellationToken cancel = default) => HostOnly();

    /// <inheritdoc />
    public Task SendFileAsync(string path, CancellationToken cancel = default)
        => throw new NotSupportedException("File transfer arrives with M5.");

    /// <inheritdoc />
    public Task SaveReceivedFileAsync(FileTransferJob job, string path, CancellationToken cancel = default)
        => throw new NotSupportedException("File transfer arrives with M5.");

    private static Task HostOnly() => throw new InvalidOperationException("Only the host can edit rooms.");

    // ------------------------------------------------------------ shutdown

    /// <inheritdoc />
    public async Task ShutdownAsync()
    {
        shuttingDown = true;
        await DisconnectAsync(null).ConfigureAwait(false);
        Status("disconnected");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);

        if (transport is IDisposable disposable)
        {
            disposable.Dispose();
        }

        writeLock.Dispose();
    }

    private async Task DisconnectAsync(string? reason)
    {
        var current = Interlocked.Exchange(ref link, null);

        if (current is not null)
        {
            current.Close();
        }

        var loop = Interlocked.Exchange(ref readLoop, null);

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The read loop reports its own reasons; nothing to add here.
            }
        }

        helloAck?.TrySetCanceled();

        if (reason is not null)
        {
            Status(reason);
        }
    }

    // ------------------------------------------------------------ the read loop

    private async Task ReadLoopAsync(IClientLink owned)
    {
        string? reason = null;

        try
        {
            while (true)
            {
                var frame = await FrameCodec.ReadAsync(owned.Input, CancellationToken.None).ConfigureAwait(false);

                if (frame is null)
                {
                    reason = "The host closed the connection.";
                    break;
                }

                Handle(frame);
            }
        }
        catch (Exception ex) when (ex is FrameException or IOException or SocketException or ObjectDisposedException)
        {
            reason = ex is FrameException frameError
                ? $"The host sent something unreadable: {frameError.Message}"
                : "Connection lost.";
        }

        var wasCurrent = ReferenceEquals(Volatile.Read(ref link), owned);
        _ = Interlocked.CompareExchange(ref link, null, owned);

        // Whatever the transcripts hold stays; who is in a room is the server's business, and there is
        // no server any more.
        state.ClearMembership();

        if (wasCurrent && !shuttingDown)
        {
            reason ??= "Connection lost.";
            ConnectionLost?.Invoke(this, new IOException(reason));
            Status(reason);
        }
    }

    private void Handle(Frame frame)
    {
        switch (frame.Type)
        {
            case FrameType.HELLO_ACK:
                helloAck?.TrySetResult(frame);
                break;

            case FrameType.ERROR:
                var problem = frame.Header.Error ?? "The host rejected that.";
                ServerError?.Invoke(this, problem);
                Status(problem);
                break;

            default:
                // ROOM_LIST, MEMBER_LIST and the traffic frames.
                if (state.Apply(frame) is { } message)
                {
                    MessageReceived?.Invoke(this, message);
                }

                break;
        }
    }

    // ---------------------------------------------------------------- writing

    private async Task WriteOrThrowAsync(Frame frame, CancellationToken cancel)
    {
        if (link is null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        try
        {
            await WriteAsync(frame, cancel).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            throw new InvalidOperationException("Connection lost.", ex);
        }
    }

    /// <summary>
    /// One frame at a time on the wire. Two messages written by interleaving half-frames would be
    /// unreadable at the other end, and the UI can raise SendAsync faster than one call completes.
    /// </summary>
    private async Task WriteAsync(Frame frame, CancellationToken cancel)
    {
        var current = link;

        if (current is null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        await writeLock.WaitAsync(cancel).ConfigureAwait(false);

        try
        {
            await FrameCodec.WriteAsync(current.Output, frame, cancel).ConfigureAwait(false);
            await current.Output.FlushAsync(cancel).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private void Status(string text) => StatusChanged?.Invoke(this, text);

    public override string ToString() => $"{Nickname} → {Endpoint}";
}
