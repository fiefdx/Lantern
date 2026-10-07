using System.Net.Sockets;
using Lantern.Domain;
using Lantern.Transport;

namespace Lantern.Core;

/// <summary>
/// The client side of a chat (T2.5): connects, introduces itself, then spends its life in a read loop
/// turning frames into state and events. It is an <see cref="IChatService"/>, so a window cannot tell
/// it apart from a host except through <see cref="IsHost"/>.
/// <para>
/// The read loop is the only writer of <see cref="Rooms"/> and <see cref="Members"/>, and it runs on
/// a thread-pool thread. That is why those two are snapshots: a window must never enumerate them
/// while a MEMBER_LIST frame is half applied.
/// </para>
/// </summary>
public sealed class ChatClient : IChatService, IAsyncDisposable
{
    private readonly IBtClient transport;
    private readonly object gate = new();
    private readonly Dictionary<string, Room> roomsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Room> rooms = [];
    private readonly List<User> members = [];
    private readonly SemaphoreSlim writeLock = new(1, 1);

    private IClientLink? link;
    private Task? readLoop;
    private TaskCompletionSource<Frame>? helloAck;
    private string? currentRoomName;
    private bool shuttingDown;

    public ChatClient(string nickname, IBtClient? transport = null)
    {
        Nickname = ValidationRules.Normalize(nickname);

        if (ValidationRules.NicknameError(Nickname) is { } error)
        {
            throw new ArgumentException(error, nameof(nickname));
        }

        this.transport = transport ?? new BtLoopbackClient();
    }

    /// <inheritdoc />
    public string Nickname { get; }

    /// <inheritdoc />
    public bool IsHost => false;

    /// <inheritdoc />
    public IReadOnlyList<Room> Rooms
    {
        get
        {
            lock (gate)
            {
                return rooms.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<User> Members
    {
        get
        {
            lock (gate)
            {
                return members.ToArray();
            }
        }
    }

    /// <summary>The server this client is, or was, talking to.</summary>
    public ServerEndpoint? Endpoint { get; private set; }

    /// <summary>Room this client is in, or null. Mirrors what the server believes.</summary>
    public string? CurrentRoomName
    {
        get
        {
            lock (gate)
            {
                return currentRoomName;
            }
        }
    }

    public bool IsConnected => link is not null;

    /// <inheritdoc />
    public event EventHandler<string>? StatusChanged;

    /// <inheritdoc />
    public event EventHandler<Message>? MessageReceived;

    /// <inheritdoc />
    public event EventHandler? RoomsChanged;

    /// <inheritdoc />
    public event EventHandler? MembersChanged;

    // Nothing on a client raises these until file transfer lands in M5 (T5.2, T5.3); they are part
    // of the interface the UI binds to, so they exist now and stay quiet until then.
#pragma warning disable CS0067
    /// <inheritdoc />
    public event EventHandler<FileTransferJob>? IncomingFile;

    /// <inheritdoc />
    public event EventHandler<string>? FileSaved;
#pragma warning restore CS0067

    /// <summary>
    /// The connection ended on its own (scenario 6). The exception's message is the reason, worded
    /// for the status bar rather than for a log.
    /// </summary>
    public event EventHandler<Exception>? ConnectionLost;

    /// <summary>The host refused something (an ERROR frame). Shown in the status bar.</summary>
    public event EventHandler<string>? ServerError;

    // ------------------------------------------------------------- connection

    /// <summary>
    /// Connects, says HELLO, waits for HELLO_ACK and starts the read loop (T2.5).
    /// </summary>
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

        readLoop = Task.Run(() => ReadLoopAsync(newLink), CancellationToken.None);

        await WriteAsync(Frame.Hello(Nickname), cancel).ConfigureAwait(false);

        var completed = await Task.WhenAny(ack.Task, Task.Delay(LanternProtocol.HandshakeTimeout, cancel)).ConfigureAwait(false);

        if (completed != ack.Task)
        {
            await DisconnectAsync("The server did not answer the Lantern handshake.").ConfigureAwait(false);
            throw new InvalidOperationException("The server did not answer the Lantern handshake. Is it a Lantern host?");
        }

        var frame = await ack.Task.ConfigureAwait(false);

        // HELLO_ACK echoes the nickname back; the server's display name comes from discovery (T2.6),
        // not from the handshake, so there is nothing to update on Endpoint here.
        _ = frame;
        Status($"connected to {Endpoint}");
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancel = default)
        => throw new InvalidOperationException("A client does not listen for connections.");

    /// <inheritdoc />
    public async Task JoinRoomAsync(string? name, CancellationToken cancel = default)
    {
        var wanted = ValidationRules.Normalize(name);

        lock (gate)
        {
            currentRoomName = wanted.Length == 0 ? null : wanted;
        }

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

        lock (gate)
        {
            members.Clear();
            currentRoomName = null;
        }

        MembersChanged?.Invoke(this, EventArgs.Empty);

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

            case FrameType.ROOM_LIST:
                ApplyRoomList(frame.Header.Rooms);
                break;

            case FrameType.MEMBER_LIST:
                ApplyMemberList(frame.Header.Room, frame.Header.Members);
                break;

            case FrameType.TEXT:
            case FrameType.PICTURE:
            case FrameType.FILE_META:
            case FrameType.SYSTEM_NOTICE:
                if (frame.ToMessage() is { } message)
                {
                    Record(message);
                    MessageReceived?.Invoke(this, message);
                }

                break;

            case FrameType.ERROR:
                var problem = frame.Header.Error ?? "The host rejected that.";
                ServerError?.Invoke(this, problem);
                Status(problem);
                break;

            default:
                // A client has no use for HELLO, JOIN_ROOM, FILE_CHUNK or FILE_DONE frames.
                break;
        }
    }

    private void ApplyRoomList(IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return;
        }

        string? notice = null;

        lock (gate)
        {
            rooms.Clear();

            foreach (var name in names)
            {
                // Rooms are kept by name across list updates so a room's transcript survives a rename
                // of the list; a room that disappeared from the list stops being shown but is not
                // thrown away, in case the host re-creates it.
                if (!roomsByName.TryGetValue(name, out var room))
                {
                    room = new Room(name);
                    roomsByName[name] = room;
                }

                rooms.Add(room);
            }

            if (currentRoomName is not null && !roomsByName.ContainsKey(currentRoomName))
            {
                notice = currentRoomName;
                currentRoomName = null;
            }
        }

        _ = notice;
        RoomsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyMemberList(string? roomName, IReadOnlyList<FrameMember>? incoming)
    {
        lock (gate)
        {
            members.Clear();

            if (incoming is not null)
            {
                members.AddRange(incoming.Select(m => new User(m.ConnectionId, m.Nickname)));
            }

            // The list is authoritative about which room it describes, including the empty list that
            // says "nobody is anywhere": without the else, a list for the room you have just left —
            // still in flight when you left — would put you back inside it.
            currentRoomName = string.IsNullOrWhiteSpace(roomName) ? null : roomName;
        }

        MembersChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps a per-room history so re-entering a room replays it locally (F6).</summary>
    private void Record(Message message)
    {
        if (string.IsNullOrWhiteSpace(message.RoomName))
        {
            return;
        }

        lock (gate)
        {
            if (!roomsByName.TryGetValue(message.RoomName, out var room))
            {
                room = new Room(message.RoomName);
                roomsByName[message.RoomName] = room;
            }

            room.Append(message);
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
